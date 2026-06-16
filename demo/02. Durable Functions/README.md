# Demo 02 - Durable Functions device provisioning

This demo accepts the same employee JSON payload as Demo 01 and starts a Durable Functions workflow that simulates device provisioning.

## Start the workflow

```powershell
func start
```

Send the employee payload to:

```text
POST http://localhost:7071/api/devices/provision
```

The response contains Durable Functions status URLs. Open the `statusQueryGetUri` while the workflow is running to see `customStatus` update through these dummy steps:

1. Order device
2. Install Windows
3. Install applications
4. Apply policies

Each step waits for about two seconds to make the progress visible during the demo.
