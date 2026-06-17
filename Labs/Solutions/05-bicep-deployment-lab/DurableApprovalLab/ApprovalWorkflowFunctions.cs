using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DurableApprovalLab;

public static class ApprovalWorkflowFunctions
{
    private const string ApprovalEventName = "approval_response";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, int> NotificationAttempts = new();
    private static readonly ConcurrentDictionary<string, string> DecisionSubmissions = new();

    [Function(nameof(StartApproval))]
    public static async Task<HttpResponseData> StartApproval(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "approvals")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(StartApproval));

        ApprovalRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ApprovalRequest>(req.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid approval request JSON.");
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "InvalidJson", "Request body must be valid JSON.");
        }

        if (request is null)
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "MissingBody", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "MissingRequestId", "requestId is required for idempotent starts.");
        }

        string instanceId = request.RequestId.Trim();
        if (!IsValidInstanceId(instanceId))
        {
            return await ProblemAsync(
                req,
                HttpStatusCode.BadRequest,
                "InvalidRequestId",
                "requestId can contain only letters, numbers, hyphens, and underscores, and must be 100 characters or fewer.");
        }

        ApprovalRequest normalizedRequest = request with
        {
            RequestId = instanceId,
            TimeoutMinutes = request.TimeoutMinutes <= 0 ? 5 : request.TimeoutMinutes
        };

        OrchestrationMetadata? existingInstance = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false);
        if (existingInstance is not null)
        {
            HttpStatusCode statusCode = IsTerminal(existingInstance.RuntimeStatus)
                ? HttpStatusCode.OK
                : HttpStatusCode.Accepted;
            return await CreateManagementResponseAsync(req, client, instanceId, statusCode, "existing-instance");
        }

        string scheduledInstanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ApprovalWorkflow),
            normalizedRequest,
            new StartOrchestrationOptions { InstanceId = instanceId });

        logger.LogInformation(
            "Started approval workflow {InstanceId} for request {RequestId}.",
            scheduledInstanceId,
            normalizedRequest.RequestId);

        return await client.CreateCheckStatusResponseAsync(req, scheduledInstanceId);
    }

    [Function(nameof(ApprovalWorkflow))]
    public static async Task<ApprovalResult> ApprovalWorkflow(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(ApprovalWorkflow));

        ApprovalRequest request = context.GetInput<ApprovalRequest>()
            ?? throw new InvalidOperationException("Approval request input is required.");

        logger.LogInformation("Running approval workflow for request {RequestId}.", request.RequestId);
        TaskOptions activityRetryOptions = CreateActivityRetryOptions();

        try
        {
            ValidationResult validation = await context.CallActivityAsync<ValidationResult>(
                nameof(ValidateApprovalRequest),
                input: request,
                options: activityRetryOptions);

            if (!validation.IsValid)
            {
                context.SetCustomStatus(new
                {
                    state = "Invalid",
                    request.RequestId,
                    validation.Reason
                });

                return new ApprovalResult
                {
                    RequestId = request.RequestId,
                    Status = "Invalid",
                    Message = validation.Reason ?? "Request is invalid.",
                    CompletedAtUtc = context.CurrentUtcDateTime
                };
            }

            if (validation.AutoApprove)
            {
                var decision = new ApprovalDecision
                {
                    DecisionId = "system-auto-approval",
                    Approved = true,
                    Approver = "system",
                    Comments = validation.Reason ?? "Auto-approved."
                };

                return await context.CallActivityAsync<ApprovalResult>(
                    nameof(FinalizeApprovalRequest),
                    input: new FinalizeApprovalInput
                    {
                        Request = request,
                        Decision = decision,
                        FinalStatus = "AutoApproved",
                        CompletedAtUtc = context.CurrentUtcDateTime
                    },
                    options: activityRetryOptions);
            }

            DateTime deadline = context.CurrentUtcDateTime.AddMinutes(request.TimeoutMinutes);

            await context.CallActivityAsync(
                nameof(CreateApprovalTask),
                input: new ApprovalNotification
                {
                    RequestId = request.RequestId,
                    InstanceId = context.InstanceId,
                    Requester = request.Requester,
                    Item = request.Item,
                    Amount = request.Amount,
                    DeadlineUtc = deadline,
                    SimulateTransientFailure = request.SimulateNotificationFailure
                },
                options: activityRetryOptions);

            context.SetCustomStatus(new
            {
                state = "WaitingForApproval",
                request.RequestId,
                context.InstanceId,
                deadlineUtc = deadline
            });

            while (true)
            {
                using var timeoutCts = new CancellationTokenSource();
                Task<ApprovalDecision> approvalTask = context.WaitForExternalEvent<ApprovalDecision>(ApprovalEventName);
                Task timeoutTask = context.CreateTimer(deadline, timeoutCts.Token);

                Task winner = await Task.WhenAny(approvalTask, timeoutTask);

                if (winner == timeoutTask)
                {
                    var timeoutDecision = new ApprovalDecision
                    {
                        DecisionId = "system-timeout",
                        Approved = false,
                        Approver = "system",
                        Comments = "No approval response received before the deadline."
                    };

                    return await context.CallActivityAsync<ApprovalResult>(
                        nameof(FinalizeApprovalRequest),
                        input: new FinalizeApprovalInput
                        {
                            Request = request,
                            Decision = timeoutDecision,
                            FinalStatus = "TimedOut",
                            CompletedAtUtc = context.CurrentUtcDateTime
                        },
                        options: activityRetryOptions);
                }

                timeoutCts.Cancel();

                ApprovalDecision decision = approvalTask.Result;
                if (string.IsNullOrWhiteSpace(decision.DecisionId))
                {
                    context.SetCustomStatus(new
                    {
                        state = "WaitingForApproval",
                        request.RequestId,
                        context.InstanceId,
                        deadlineUtc = deadline,
                        ignoredDecision = "Missing decisionId"
                    });

                    continue;
                }

                string finalStatus = decision.Approved ? "Approved" : "Rejected";

                return await context.CallActivityAsync<ApprovalResult>(
                    nameof(FinalizeApprovalRequest),
                    input: new FinalizeApprovalInput
                    {
                        Request = request,
                        Decision = decision,
                        FinalStatus = finalStatus,
                        CompletedAtUtc = context.CurrentUtcDateTime
                    },
                    options: activityRetryOptions);
            }
        }
        catch (TaskFailedException ex)
        {
            context.SetCustomStatus(new
            {
                state = "Failed",
                request.RequestId,
                errorType = ex.FailureDetails?.ErrorType ?? ex.GetType().Name,
                errorMessage = ex.FailureDetails?.ErrorMessage ?? ex.Message
            });

            return new ApprovalResult
            {
                RequestId = request.RequestId,
                Status = "Failed",
                Message = $"The workflow failed after retries were exhausted. {ex.FailureDetails?.ErrorMessage ?? ex.Message}",
                CompletedAtUtc = context.CurrentUtcDateTime
            };
        }
    }

    [Function(nameof(SubmitDecision))]
    public static async Task<HttpResponseData> SubmitDecision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "approvals/{instanceId}/decision")] HttpRequestData req,
        string instanceId,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(SubmitDecision));

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "MissingInstanceId", "The orchestration instance ID is required.");
        }

        ApprovalDecision? decision;
        try
        {
            decision = await JsonSerializer.DeserializeAsync<ApprovalDecision>(req.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid approval decision JSON for instance {InstanceId}.", instanceId);
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "InvalidJson", "Request body must be valid JSON.");
        }

        if (decision is null)
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "MissingBody", "Request body must be a valid approval decision.");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionId))
        {
            return await ProblemAsync(req, HttpStatusCode.BadRequest, "MissingDecisionId", "decisionId is required for idempotent decisions.");
        }

        string normalizedDecisionId = decision.DecisionId.Trim();
        OrchestrationMetadata? metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false);
        if (metadata is null)
        {
            return await ProblemAsync(req, HttpStatusCode.NotFound, "InstanceNotFound", $"No orchestration instance '{instanceId}' was found.");
        }

        if (IsTerminal(metadata.RuntimeStatus))
        {
            return await CreateManagementResponseAsync(req, client, instanceId, HttpStatusCode.OK, "workflow-not-accepting-decisions");
        }

        string decisionSubmissionKey = $"{instanceId}:{normalizedDecisionId}";
        if (!DecisionSubmissions.TryAdd(decisionSubmissionKey, normalizedDecisionId))
        {
            return await CreateManagementResponseAsync(req, client, instanceId, HttpStatusCode.Accepted, "duplicate-decision");
        }

        ApprovalDecision normalizedDecision = decision with
        {
            DecisionId = normalizedDecisionId,
            Approver = string.IsNullOrWhiteSpace(decision.Approver) ? "anonymous" : decision.Approver.Trim()
        };

        await client.RaiseEventAsync(instanceId, ApprovalEventName, normalizedDecision);

        logger.LogInformation(
            "Raised {EventName} with decision {DecisionId} for approval workflow {InstanceId}.",
            ApprovalEventName,
            normalizedDecision.DecisionId,
            instanceId);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            message = "Decision submitted.",
            instanceId,
            decisionId = normalizedDecision.DecisionId,
            eventName = ApprovalEventName
        });

        return response;
    }

    [Function(nameof(ValidateApprovalRequest))]
    public static ValidationResult ValidateApprovalRequest(
        [ActivityTrigger] ApprovalRequest request,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(ValidateApprovalRequest));
        logger.LogInformation("Validating approval request {RequestId}.", request.RequestId);

        if (string.IsNullOrWhiteSpace(request.Requester))
        {
            return ValidationResult.Invalid("Requester is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Item))
        {
            return ValidationResult.Invalid("Item is required.");
        }

        if (request.Amount <= 0)
        {
            return ValidationResult.Invalid("Amount must be greater than zero.");
        }

        if (request.Amount <= 100)
        {
            return ValidationResult.Valid(true, "Auto-approved because the amount is 100 or less.");
        }

        return ValidationResult.Valid(false, "Approval required because the amount is greater than 100.");
    }

    [Function(nameof(CreateApprovalTask))]
    public static void CreateApprovalTask(
        [ActivityTrigger] ApprovalNotification notification,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(CreateApprovalTask));
        int attempt = NotificationAttempts.AddOrUpdate(notification.RequestId, 1, (_, current) => current + 1);

        if (notification.SimulateTransientFailure && attempt == 1)
        {
            logger.LogWarning(
                "Simulating transient notification failure for request {RequestId} on attempt {Attempt}.",
                notification.RequestId,
                attempt);

            throw new InvalidOperationException("Simulated transient notification failure.");
        }

        logger.LogWarning(
            "Approval required for request {RequestId}. Instance ID: {InstanceId}. " +
            "Submit the decision with POST /api/approvals/{InstanceId}/decision before {DeadlineUtc:o}. Attempt: {Attempt}.",
            notification.RequestId,
            notification.InstanceId,
            notification.InstanceId,
            notification.DeadlineUtc,
            attempt);

        logger.LogInformation(
            "Requester {Requester} asked for {Item} costing {Amount}.",
            notification.Requester,
            notification.Item,
            notification.Amount);
    }

    [Function(nameof(FinalizeApprovalRequest))]
    public static ApprovalResult FinalizeApprovalRequest(
        [ActivityTrigger] FinalizeApprovalInput input,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(FinalizeApprovalRequest));

        if (input.Request.SimulatePermanentFailure)
        {
            logger.LogError("Simulating permanent finalization failure for request {RequestId}.", input.Request.RequestId);
            throw new InvalidOperationException("Simulated permanent finalization failure.");
        }

        string message = input.FinalStatus switch
        {
            "Approved" => $"Approved by {input.Decision.Approver}.",
            "Rejected" => $"Rejected by {input.Decision.Approver}.",
            "TimedOut" => "Rejected automatically because the approval timed out.",
            "AutoApproved" => "Approved automatically by policy.",
            _ => $"Workflow finished with status {input.FinalStatus}."
        };

        logger.LogInformation(
            "Finalized request {RequestId} with status {Status}.",
            input.Request.RequestId,
            input.FinalStatus);

        return new ApprovalResult
        {
            RequestId = input.Request.RequestId,
            Status = input.FinalStatus,
            Message = message,
            Decision = input.Decision,
            CompletedAtUtc = input.CompletedAtUtc
        };
    }

    private static TaskOptions CreateActivityRetryOptions()
    {
        var retryPolicy = new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromSeconds(30),
            retryTimeout: TimeSpan.FromMinutes(2));

        return TaskOptions.FromRetryPolicy(retryPolicy);
    }

    private static bool IsTerminal(OrchestrationRuntimeStatus status) =>
        status is OrchestrationRuntimeStatus.Completed
            or OrchestrationRuntimeStatus.Failed
            or OrchestrationRuntimeStatus.Terminated;

    private static bool IsValidInstanceId(string instanceId) =>
        instanceId.Length <= 100 &&
        instanceId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static async Task<HttpResponseData> ProblemAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string code,
        string message)
    {
        HttpResponseData response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new ErrorResponse(code, message));
        return response;
    }

    private static async Task<HttpResponseData> CreateManagementResponseAsync(
        HttpRequestData req,
        DurableTaskClient client,
        string instanceId,
        HttpStatusCode statusCode,
        string idempotencyResult)
    {
        HttpResponseData response = req.CreateResponse(statusCode);
        response.Headers.Add("x-idempotency-result", idempotencyResult);
        await response.WriteAsJsonAsync(client.CreateHttpManagementPayload(instanceId, req));
        return response;
    }
}

public record ApprovalRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string Requester { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string BusinessJustification { get; init; } = string.Empty;
    public int TimeoutMinutes { get; init; } = 5;
    public bool SimulateNotificationFailure { get; init; }
    public bool SimulatePermanentFailure { get; init; }
}

public record ApprovalDecision
{
    public string DecisionId { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public string Approver { get; init; } = string.Empty;
    public string? Comments { get; init; }
}

public record ApprovalNotification
{
    public string RequestId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string Requester { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime DeadlineUtc { get; init; }
    public bool SimulateTransientFailure { get; init; }
}

public record FinalizeApprovalInput
{
    public ApprovalRequest Request { get; init; } = new();
    public ApprovalDecision Decision { get; init; } = new();
    public string FinalStatus { get; init; } = string.Empty;
    public DateTime CompletedAtUtc { get; init; }
}

public record ApprovalResult
{
    public string RequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ApprovalDecision? Decision { get; init; }
    public DateTime CompletedAtUtc { get; init; }
}

public record ValidationResult
{
    public bool IsValid { get; init; }
    public bool AutoApprove { get; init; }
    public string? Reason { get; init; }

    public static ValidationResult Invalid(string reason) =>
        new() { IsValid = false, AutoApprove = false, Reason = reason };

    public static ValidationResult Valid(bool autoApprove, string reason) =>
        new() { IsValid = true, AutoApprove = autoApprove, Reason = reason };
}

public record ErrorResponse(string Code, string Message);