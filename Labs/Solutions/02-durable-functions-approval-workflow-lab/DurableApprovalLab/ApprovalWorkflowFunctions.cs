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

    [Function(nameof(StartApproval))]
    public static async Task<HttpResponseData> StartApproval(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "approvals")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        ILogger logger = executionContext.GetLogger(nameof(StartApproval));

        ApprovalRequest? request = await JsonSerializer.DeserializeAsync<ApprovalRequest>(req.Body, JsonOptions);
        if (request is null)
        {
            HttpResponseData badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Request body must be a valid approval request.");
            return badRequest;
        }

        ApprovalRequest normalizedRequest = request with
        {
            RequestId = string.IsNullOrWhiteSpace(request.RequestId)
                ? Guid.NewGuid().ToString("N")
                : request.RequestId,
            TimeoutMinutes = request.TimeoutMinutes <= 0 ? 5 : request.TimeoutMinutes
        };

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ApprovalWorkflow),
            normalizedRequest);

        logger.LogInformation(
            "Started approval workflow {InstanceId} for request {RequestId}.",
            instanceId,
            normalizedRequest.RequestId);

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    [Function(nameof(ApprovalWorkflow))]
    public static async Task<ApprovalResult> ApprovalWorkflow(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(ApprovalWorkflow));

        ApprovalRequest request = context.GetInput<ApprovalRequest>()
            ?? throw new InvalidOperationException("Approval request input is required.");

        logger.LogInformation("Running approval workflow for request {RequestId}.", request.RequestId);

        ValidationResult validation = await context.CallActivityAsync<ValidationResult>(
            nameof(ValidateApprovalRequest),
            request);

        if (!validation.IsValid)
        {
            context.SetCustomStatus(new
            {
                state = "Invalid",
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
                Approved = true,
                Approver = "system",
                Comments = validation.Reason ?? "Auto-approved."
            };

            return await context.CallActivityAsync<ApprovalResult>(
                nameof(FinalizeApprovalRequest),
                new FinalizeApprovalInput
                {
                    Request = request,
                    Decision = decision,
                    FinalStatus = "AutoApproved",
                    CompletedAtUtc = context.CurrentUtcDateTime
                });
        }

        DateTime deadline = context.CurrentUtcDateTime.AddMinutes(request.TimeoutMinutes);

        await context.CallActivityAsync(
            nameof(CreateApprovalTask),
            new ApprovalNotification
            {
                RequestId = request.RequestId,
                InstanceId = context.InstanceId,
                Requester = request.Requester,
                Item = request.Item,
                Amount = request.Amount,
                DeadlineUtc = deadline
            });

        context.SetCustomStatus(new
        {
            state = "WaitingForApproval",
            request.RequestId,
            context.InstanceId,
            deadlineUtc = deadline
        });

        using var timeoutCts = new CancellationTokenSource();
        Task<ApprovalDecision> approvalTask = context.WaitForExternalEvent<ApprovalDecision>(ApprovalEventName);
        Task timeoutTask = context.CreateTimer(deadline, timeoutCts.Token);

        Task winner = await Task.WhenAny(approvalTask, timeoutTask);

        if (winner == approvalTask)
        {
            timeoutCts.Cancel();

            ApprovalDecision decision = approvalTask.Result;
            string finalStatus = decision.Approved ? "Approved" : "Rejected";

            return await context.CallActivityAsync<ApprovalResult>(
                nameof(FinalizeApprovalRequest),
                new FinalizeApprovalInput
                {
                    Request = request,
                    Decision = decision,
                    FinalStatus = finalStatus,
                    CompletedAtUtc = context.CurrentUtcDateTime
                });
        }

        var timeoutDecision = new ApprovalDecision
        {
            Approved = false,
            Approver = "system",
            Comments = "No approval response received before the deadline."
        };

        return await context.CallActivityAsync<ApprovalResult>(
            nameof(FinalizeApprovalRequest),
            new FinalizeApprovalInput
            {
                Request = request,
                Decision = timeoutDecision,
                FinalStatus = "TimedOut",
                CompletedAtUtc = context.CurrentUtcDateTime
            });
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
            HttpResponseData badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("The orchestration instance ID is required.");
            return badRequest;
        }

        ApprovalDecision? decision = await JsonSerializer.DeserializeAsync<ApprovalDecision>(req.Body, JsonOptions);
        if (decision is null)
        {
            HttpResponseData badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Request body must be a valid approval decision.");
            return badRequest;
        }

        ApprovalDecision normalizedDecision = decision with
        {
            Approver = string.IsNullOrWhiteSpace(decision.Approver) ? "anonymous" : decision.Approver
        };

        await client.RaiseEventAsync(instanceId, ApprovalEventName, normalizedDecision);

        logger.LogInformation(
            "Raised {EventName} for approval workflow {InstanceId}.",
            ApprovalEventName,
            instanceId);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            message = "Decision submitted.",
            instanceId,
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

        logger.LogWarning(
            "Approval required for request {RequestId}. Instance ID: {InstanceId}. " +
            "Submit the decision with POST /api/approvals/{InstanceId}/decision before {DeadlineUtc:o}.",
            notification.RequestId,
            notification.InstanceId,
            notification.InstanceId,
            notification.DeadlineUtc);

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
}

public record ApprovalRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string Requester { get; init; } = string.Empty;
    public string Item { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string BusinessJustification { get; init; } = string.Empty;
    public int TimeoutMinutes { get; init; } = 5;
}

public record ApprovalDecision
{
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