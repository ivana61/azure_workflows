using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FunctionsDemo;

public sealed class GenerateEmployeeIdFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<GenerateEmployeeIdFunction> _logger;

    public GenerateEmployeeIdFunction(ILogger<GenerateEmployeeIdFunction> logger)
    {
        _logger = logger;
    }

    [Function("GenerateEmployeeId")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "employees/generate-id")] HttpRequest request)
    {
        GenerateEmployeeIdRequest? payload;

        try
        {
            using var document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: request.HttpContext.RequestAborted);

            // Ignore workflow connectivity test payloads and return 200.
            if (IsWorkflowTestPayload(document.RootElement))
            {
                _logger.LogInformation("Received workflow test payload. Returning 200 without processing.");
                return new OkResult();
            }

            payload = document.RootElement.TryGetProperty("employee", out var employeeElement)
                ? employeeElement.Deserialize<GenerateEmployeeIdRequest>(JsonOptions)
                : document.RootElement.Deserialize<GenerateEmployeeIdRequest>(JsonOptions);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                error = "Request body must be valid JSON."
            });
        }

        var name = NormalizeNamePart(payload?.Name);

        if (name.Length < 3)
        {
            return new BadRequestObjectResult(new
            {
                error = "name must contain at least 3 letters."
            });
        }

        var employeeId = $"{name[..3]}{RandomNumberGenerator.GetInt32(0, 100):00}"
            .ToUpperInvariant();

        _logger.LogInformation(
            "Generated employee ID {EmployeeId} for {Name}.",
            employeeId,
            name);

        return new OkObjectResult(new
        {
            employeeId,
            name
        });
    }

    private static string NormalizeNamePart(string? value)
    {
        var source = value ?? string.Empty;
        return new string(source.Where(char.IsLetter).ToArray());
    }

    private static bool IsWorkflowTestPayload(JsonElement root)
    {
        return root.TryGetProperty("source", out _)
            && root.TryGetProperty("event", out _)
            && root.TryGetProperty("target", out _)
            && root.TryGetProperty("message", out _)
            && root.TryGetProperty("timestamp", out _)
            && !root.TryGetProperty("name", out _)
            && !root.TryGetProperty("employee", out _);
    }

}

public sealed record GenerateEmployeeIdRequest(
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
