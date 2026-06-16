using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FunctionsDemo;

public sealed class RegisterDeviceFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<RegisterDeviceFunction> _logger;

    public RegisterDeviceFunction(ILogger<RegisterDeviceFunction> logger)
    {
        _logger = logger;
    }

    [Function("RegisterDevice")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devices/register")] HttpRequest request)
    {
        Employee? employee;

        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: request.HttpContext.RequestAborted);

            employee = document.RootElement.TryGetProperty("employee", out var employeeElement)
                ? employeeElement.Deserialize<Employee>(JsonOptions)
                : document.RootElement.Deserialize<Employee>(JsonOptions);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                error = "Request body must be valid JSON."
            });
        }

        if (string.IsNullOrWhiteSpace(employee?.Id))
        {
            return new BadRequestObjectResult(new
            {
                error = "Request body must contain employee id."
            });
        }

        var employeeId = employee.Id;
        var deviceId = GenerateWindowsLaptopDeviceId();

        _logger.LogInformation(
            "Device registration started for employee {EmployeeId} with device {DeviceId}.",
            employeeId,
            deviceId);

        return new OkObjectResult(new
        {
            message = $"device registration for {employeeId} started for device {deviceId}",
            employeeId,
            employeeName = employee.Name,
            deviceId,
            deviceType = "Windows laptop"
        });
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
