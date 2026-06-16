using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DurableFunctionsDemo;

public static class DeviceProvisioningWorkflow
{
    private const int TotalSteps = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DemoStepDuration = TimeSpan.FromSeconds(2);

    [Function("StartDeviceProvisioning")]
    public static async Task<HttpResponseData> StartDeviceProvisioning(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devices/provision")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(StartDeviceProvisioning));
        var (employee, error) = await ReadEmployeeAsync(request);

        if (error is not null)
        {
            return await CreateBadRequestResponseAsync(request, error);
        }

        if (string.IsNullOrWhiteSpace(employee?.Id))
        {
            return await CreateBadRequestResponseAsync(request, "Request body must contain employee id.");
        }

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(DeviceProvisioningOrchestration),
            employee);

        logger.LogInformation(
            "Started device provisioning workflow {InstanceId} for employee {EmployeeId}.",
            instanceId,
            employee.Id);

        return await client.CreateCheckStatusResponseAsync(request, instanceId);
    }

    [Function(nameof(DeviceProvisioningOrchestration))]
    public static async Task<DeviceProvisioningResult> DeviceProvisioningOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var employee = context.GetInput<Employee>()
            ?? throw new InvalidOperationException("The orchestration input must contain an employee.");

        if (string.IsNullOrWhiteSpace(employee.Id))
        {
            throw new InvalidOperationException("The orchestration input must contain employee id.");
        }

        var steps = new List<ProvisioningStepStatus>();

        context.SetCustomStatus(CreateStatus(employee, "Starting workflow", "Running", steps));

        var orderDevice = await RunStepAsync(
            context,
            employee,
            steps,
            "Order device",
            nameof(OrderDevice),
            employee);

        var workflowState = new DeviceWorkflowState(employee, orderDevice.DeviceId);

        await RunStepAsync(context, employee, steps, "Install Windows", nameof(InstallWindows), workflowState);
        await RunStepAsync(context, employee, steps, "Install applications", nameof(InstallApplications), workflowState);
        await RunStepAsync(context, employee, steps, "Apply policies", nameof(ApplyPolicies), workflowState);

        context.SetCustomStatus(CreateStatus(employee, "Workflow completed", "Completed", steps));

        return new DeviceProvisioningResult(
            employee.Id,
            employee.Name,
            workflowState.DeviceId,
            "Completed",
            $"Device provisioning completed for employee {employee.Id} on device {workflowState.DeviceId}.",
            steps);
    }

    [Function(nameof(OrderDevice))]
    public static async Task<ProvisioningStepResult> OrderDevice(
        [ActivityTrigger] Employee employee,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(OrderDevice));
        await SimulateStepAsync(logger, "Order device");

        var deviceId = GenerateWindowsLaptopDeviceId();

        return new ProvisioningStepResult(
            "Order device",
            $"Ordered Windows laptop {deviceId} for employee {employee.Id}.",
            deviceId);
    }

    [Function(nameof(InstallWindows))]
    public static async Task<ProvisioningStepResult> InstallWindows(
        [ActivityTrigger] DeviceWorkflowState state,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(InstallWindows));
        await SimulateStepAsync(logger, "Install Windows");

        return new ProvisioningStepResult(
            "Install Windows",
            $"Installed Windows 11 Enterprise on device {state.DeviceId}.",
            state.DeviceId);
    }

    [Function(nameof(InstallApplications))]
    public static async Task<ProvisioningStepResult> InstallApplications(
        [ActivityTrigger] DeviceWorkflowState state,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(InstallApplications));
        await SimulateStepAsync(logger, "Install applications");

        return new ProvisioningStepResult(
            "Install applications",
            $"Installed baseline applications on device {state.DeviceId}.",
            state.DeviceId);
    }

    [Function(nameof(ApplyPolicies))]
    public static async Task<ProvisioningStepResult> ApplyPolicies(
        [ActivityTrigger] DeviceWorkflowState state,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(ApplyPolicies));
        await SimulateStepAsync(logger, "Apply policies");

        return new ProvisioningStepResult(
            "Apply policies",
            $"Applied security and compliance policies to device {state.DeviceId}.",
            state.DeviceId);
    }

    private static async Task<ProvisioningStepResult> RunStepAsync<TInput>(
        TaskOrchestrationContext context,
        Employee employee,
        List<ProvisioningStepStatus> completedSteps,
        string stepName,
        string activityName,
        TInput input)
    {
        context.SetCustomStatus(CreateStatus(employee, stepName, "Running", completedSteps));

        var result = await context.CallActivityAsync<ProvisioningStepResult>(activityName, input);
        completedSteps.Add(new ProvisioningStepStatus(result.Name, "Completed", result.Message));

        context.SetCustomStatus(CreateStatus(employee, stepName, "Completed", completedSteps));

        return result;
    }

    private static async Task<(Employee? Employee, string? Error)> ReadEmployeeAsync(HttpRequestData request)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);

            var employee = document.RootElement.TryGetProperty("employee", out var employeeElement)
                ? employeeElement.Deserialize<Employee>(JsonOptions)
                : document.RootElement.Deserialize<Employee>(JsonOptions);

            return (employee, null);
        }
        catch (JsonException)
        {
            return (null, "Request body must be valid JSON.");
        }
    }

    private static DeviceProvisioningStatus CreateStatus(
        Employee employee,
        string currentStep,
        string currentStepStatus,
        IReadOnlyCollection<ProvisioningStepStatus> completedSteps)
    {
        return new DeviceProvisioningStatus(
            employee.Id,
            employee.Name,
            currentStep,
            currentStepStatus,
            completedSteps.Count,
            TotalSteps,
            completedSteps.ToArray());
    }

    private static async Task<HttpResponseData> CreateBadRequestResponseAsync(HttpRequestData request, string error)
    {
        var response = request.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error });
        return response;
    }

    private static async Task SimulateStepAsync(ILogger logger, string stepName)
    {
        logger.LogInformation("{StepName} started.", stepName);
        await Task.Delay(DemoStepDuration);
        logger.LogInformation("{StepName} completed.", stepName);
    }

    private static string GenerateWindowsLaptopDeviceId()
    {
        return $"WIN-LAPTOP-{RandomNumberGenerator.GetHexString(6)}";
    }
}

public sealed record Employee(
    string? Id,
    string? Name,
    string? Email,
    string? Department,
    string? Role,
    string? StartDate,
    string? Status,
    string? Manager,
    string? Description,
    string? CreatedAt,
    string? Source,
    string? SubmittedAt);

public sealed record DeviceWorkflowState(Employee Employee, string DeviceId);

public sealed record ProvisioningStepResult(string Name, string Message, string DeviceId);

public sealed record ProvisioningStepStatus(string Name, string Status, string Message);

public sealed record DeviceProvisioningStatus(
    string? EmployeeId,
    string? EmployeeName,
    string CurrentStep,
    string CurrentStepStatus,
    int CompletedSteps,
    int TotalSteps,
    IReadOnlyList<ProvisioningStepStatus> Steps);

public sealed record DeviceProvisioningResult(
    string EmployeeId,
    string? EmployeeName,
    string DeviceId,
    string Status,
    string Message,
    IReadOnlyList<ProvisioningStepStatus> Steps);
