# Azure Workflows Lab Solutions

This folder contains one solution subfolder per lab. Azure deployment and Azure resource-creation steps were intentionally skipped and are listed below.

## Solution folders

| Lab | Solution folder | Contents |
| --- | --- | --- |
| 01 | `01-azure-functions-lab` | .NET 10 isolated Azure Functions app with `Echo`, `Recurring`, and `GetSettingInfo`, plus sample `settings.json`. |
| 02 | `02-durable-functions-approval-workflow-lab` | .NET 10 isolated Durable Functions approval workflow. |
| 03 | `03-durable-error-handling-idempotency-retries-lab` | Hardened Durable workflow with idempotent starts, structured errors, retries, and idempotent decisions. |
| 04 | `04-external-triggers-web-app-lab` | Static HTML browser client for starting, checking, and approving workflows. |
| 05 | `05-bicep-deployment-lab` | Deployable copy of the hardened Durable workflow plus `infra/main.bicep`. |

## Local run prerequisites

The local smoke tests used:

```powershell
dotnet --list-sdks
func --version
az --version
node --version
npm --version
```

For timer, blob, and Durable Functions local runtime state, start Azurite with API-version checking disabled:

```powershell
Set-Location d:\Workshops\azure_workflows\Labs\Solutions
npx -y azurite --silent --skipApiVersionCheck --location .\.azurite --debug .\.azurite\debug.log
```

For Lab 01, seed the local blob used by `GetSettingInfo`:

```powershell
Set-Location d:\Workshops\azure_workflows\Labs
$devStorage = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
az storage container create --name content --connection-string $devStorage --only-show-errors
az storage blob upload --container-name content --name settings.json --file .\Solutions\01-azure-functions-lab\settings.json --connection-string $devStorage --overwrite --only-show-errors
```

## Verification completed

All .NET projects compile:

```powershell
dotnet build .\Solutions\01-azure-functions-lab\func\func.csproj
dotnet build .\Solutions\02-durable-functions-approval-workflow-lab\DurableApprovalLab\DurableApprovalLab.csproj
dotnet build .\Solutions\03-durable-error-handling-idempotency-retries-lab\DurableApprovalLab\DurableApprovalLab.csproj
dotnet build .\Solutions\05-bicep-deployment-lab\DurableApprovalLab\DurableApprovalLab.csproj
```

Lab 01 runtime checks completed:

- `Echo` returned `Hello` with HTTP 200.
- `GetSettingInfo` returned the seeded `settings.json` blob with HTTP 200.
- `Recurring` timer trigger fired and completed successfully.

Lab 02 runtime checks completed:

- Auto-approval request completed with output status `AutoApproved`.
- Human approval request accepted a decision event and completed with output status `Approved`.

Lab 03 runtime checks completed:

- Missing `requestId` returned structured HTTP 400 JSON.
- Duplicate start returned `x-idempotency-result: existing-instance`.
- Human approval with `decisionId` completed with output status `Approved`.
- A post-completion duplicate decision returned `x-idempotency-result: workflow-not-accepting-decisions`.

Lab 04 runtime checks completed:

- Served `web/index.html` from `http://127.0.0.1:5500/index.html` with HTTP 200.
- Page content contains the expected `External Approval Client` title.

Lab 05 runtime checks completed:

- `infra/main.bicep` compiled locally with `az bicep build`.
- The deployable Durable Functions app started locally and completed an auto-approved workflow.

VS Code diagnostics reported no errors under `Solutions` after the final build.

## Issues encountered and fixes

| Issue | Resolution |
| --- | --- |
| Core Tools generated .NET isolated projects with Azure Monitor OpenTelemetry export enabled, but local labs do not set `APPLICATIONINSIGHTS_CONNECTION_STRING`. | Updated each `Program.cs` to enable `UseAzureMonitorExporter()` only when that environment variable exists. |
| Core Tools suggested `dotnet run`, but direct `dotnet run` failed with missing `Functions:Worker:HostEndpoint`. | Used `func start --build` for runtime smoke tests. For stubborn working-directory cases, used `--script-root`. |
| `func start --no-build` did not discover functions because generated isolated-worker metadata was not at the app root. | Used `func start --build`, matching the lab instructions. |
| Lab 01 `GetSettingInfo` used synchronous `WriteString`, which fails with the ASP.NET-backed isolated worker in the generated .NET 10 template. | Changed the function to `async Task<HttpResponseData>` and used `WriteStringAsync`. |
| Initial Azurite upload failed because an older emulator account key was used. | Switched to the current documented `devstoreaccount1` key. |
| Durable Functions failed against Azurite because the storage client used API version `2026-02-06`, which Azurite 3.35 rejects by default. | Started Azurite with `--skipApiVersionCheck`. |
| Lab 03 duplicate-start logic changed `StatusCode` after `CreateCheckStatusResponseAsync` had already started the response. | Used `CreateHttpManagementPayload` and wrote the response manually so status and `x-idempotency-result` headers are set before the JSON body. The same fix was applied to the Lab 05 workflow copy. |
| Core Tools printed `The Azure Functions Python worker does not support windows-arm64.` during .NET isolated runs. | This warning did not block any .NET Functions build or runtime smoke test. |
| `npx http-server` printed a transitive package deprecation warning. | The static web app still served correctly with HTTP 200. |

## Azure steps skipped

### Lab 01

- Creating the Azure resource group.
- Creating the Azure Storage account.
- Creating the Azure Function App.
- Uploading `settings.json` to Azure Blob Storage. This was simulated locally with Azurite.
- Publishing with `func azure functionapp publish`.
- Validating deployed functions in the Azure portal.
- Azure resource-group cleanup.

### Lab 02

- Listing Flex Consumption regions.
- Creating Azure resource group, storage account, and Function App.
- Publishing the Durable Functions app to Azure.
- Testing the Azure-deployed workflow.
- Streaming Azure Function App logs.
- Azure resource-group cleanup.

### Lab 03

- No Azure deployment steps are included in this lab file. All implemented steps were local.

### Lab 04

- Adding CORS settings to a deployed Azure Function App.
- Calling a deployed Azure Function App from the browser client.
- Retrieving/pasting Azure Function host keys.
- Removing temporary Azure CORS settings.
- Azure resource-group cleanup.

### Lab 05

- Creating a resource group in Azure.
- Running Azure deployment what-if against a resource group.
- Deploying the Bicep template with `az deployment group create`.
- Inspecting deployed Azure resources, app settings, CORS, and role assignments.
- Publishing code to the Azure Function App.
- Testing the deployed Durable Functions API.
- Connecting the Lab 04 browser app to the deployed Function App.
- Inspecting Application Insights telemetry in Azure.
- Applying the safe infrastructure change in Azure.
- Azure resource-group cleanup.