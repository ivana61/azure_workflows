# Demo: Employee Onboarding Workflow with Azure Logic Apps

## Overview

This demo showcases a single Azure Logic App workflow designed for a **telco operator** (e.g., Proximus) that automates employee onboarding. The workflow is **HTTP-triggered** — an HR system (or manual call) sends a JSON payload containing the new employee's details including their role type. Based on the employee type in the payload, the Logic App routes to one of two flows:

**Flow 1 — Office Employee: Device Registration Request**
Processes the onboarding for an office-based employee and sends an HTTP request to a downstream **Device Registration API** to provision a corporate laptop.

**Flow 2 — Technician Employee: Safety Equipment Request**
Processes the onboarding for a field technician and sends an HTTP request to a downstream **Safety Equipment Ordering API** to issue mandatory safety gear (boots, helmet, high-visibility vest, safety glasses).

---

## Business context

Telco operators have large workforces with diverse roles — from office staff managing customer accounts and network planning to field technicians installing fiber, maintaining cell towers, and troubleshooting on-site equipment. Each role has different onboarding needs:

- **Office employees** need a laptop, corporate email, VPN access, and software licenses.
- **Field technicians** need safety equipment, a rugged device, fleet vehicle assignment, and mandatory safety training certification.

Manual onboarding creates delays, missed steps, and compliance gaps. Automating these flows with Logic Apps ensures:

- Consistent provisioning within SLA.
- Audit trail for compliance (safety regulations, corporate device policies).
- Integration with existing HR, IT asset management, and procurement systems.

---

## Architecture

```text
┌─────────────────────────────────────────────────────────────────────┐
│            HR System / Manual Call (HTTP POST)                        │
│                                                                      │
│  Payload: { employeeType, employeeName, employeeId, department, ... }│
└─────────────────────┬───────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│              Logic App: Employee Onboarding                           │
│                                                                      │
│  [HTTP Request Trigger] ──► [Condition: employeeType]                │
│                                    │                                 │
│                    ┌───────────────┴───────────────┐                 │
│                    │                               │                 │
│          employeeType = "Office"        employeeType = "Technician"  │
│                    │                               │                 │
│                    ▼                               ▼                 │
│  ┌─────────────────────────┐    ┌──────────────────────────────┐    │
│  │ [Compose] Build device  │    │ [Compose] Build safety       │    │
│  │  registration payload   │    │  equipment order payload     │    │
│  │                         │    │                              │    │
│  │ [HTTP POST] ──────────► │    │ [HTTP POST] ──────────────► │    │
│  │  Device Registration    │    │  Safety Equipment Ordering   │    │
│  │  API                    │    │  API                         │    │
│  │                         │    │                              │    │
│  │ [Response] 200 + result │    │ [Response] 200 + result      │    │
│  └─────────────────────────┘    └──────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
                      │                               │
                      ▼                               ▼
         ┌────────────────────────┐    ┌──────────────────────────────┐
         │ Device Registration    │    │ Safety Equipment Ordering     │
         │ API (downstream)       │    │ API (downstream)              │
         └────────────────────────┘    └──────────────────────────────┘
```

---

## HTTP trigger payload

The Logic App expects a POST request with the following JSON structure:

```json
{
  "employeeId": "EMP-2026-0847",
  "employeeName": "Sophie Martin",
  "employeeType": "Office",
  "department": "Network Planning",
  "startDate": "2026-06-23",
  "managerEmail": "manager@proximus.be",
  "site": "Brussels HQ",
  "bootSize": null,
  "clothingSize": null,
  "specialization": null
}
```

For a technician:

```json
{
  "employeeId": "EMP-2026-0848",
  "employeeName": "Thomas Dubois",
  "employeeType": "Technician",
  "department": "Field Operations",
  "startDate": "2026-06-23",
  "managerEmail": "sitemanager@proximus.be",
  "site": "Brussels South",
  "bootSize": 43,
  "clothingSize": "L",
  "specialization": "Fiber"
}
```

---

## Flow 1: Office Employee — Device Registration Request

### Workflow steps

```text
1. [Trigger] HTTP POST received with employee payload
       │
2. [Condition] Is employeeType == "Office"?
       │ Yes
       ▼
3. [Compose] Build device registration payload
       │   {
       │     "requestId": "DEV-{employeeId}-{timestamp}",
       │     "employeeId": "{employeeId}",
       │     "employeeName": "{employeeName}",
       │     "department": "{department}",
       │     "deviceType": "Standard Laptop",
       │     "software": ["Microsoft 365", "Teams", "VPN Client", "Intune"],
       │     "deliveryLocation": "{site}",
       │     "requiredBy": "{startDate}"
       │   }
       │
4. [HTTP] POST to Device Registration API
       │   URL: https://<device-registration-api>/api/register
       │   Body: Compose output from step 3
       │   Retry policy: Exponential, 3 attempts
       │
5. [Response] Return 200 to caller
       │   Body:
       │   {
       │     "status": "DeviceRegistrationSubmitted",
       │     "employeeId": "{employeeId}",
       │     "trackingId": "{response from Device Registration API}",
       │     "message": "Laptop registration request submitted for {employeeName}"
       │   }
```

---

## Flow 2: Technician Employee — Safety Equipment Request

### Workflow steps

```text
1. [Trigger] HTTP POST received with employee payload
       │
2. [Condition] Is employeeType == "Technician"?
       │ Yes
       ▼
3. [Compose] Build safety equipment order payload
       │   {
       │     "orderId": "SAFETY-{employeeId}-{timestamp}",
       │     "employeeId": "{employeeId}",
       │     "employeeName": "{employeeName}",
       │     "site": "{site}",
       │     "specialization": "{specialization}",
       │     "equipment": [
       │       { "item": "Safety Boots", "size": "{bootSize}" },
       │       { "item": "Safety Helmet", "size": "Universal" },
       │       { "item": "High-Visibility Vest", "size": "{clothingSize}" },
       │       { "item": "Safety Glasses", "size": "Universal" }
       │     ],
       │     "additionalEquipment": (based on specialization),
       │     "deliverTo": "{site}",
       │     "requiredBy": "{startDate}"
       │   }
       │
       │   Additional equipment by specialization:
       │     • Fiber: + Harness
       │     • Tower: + Fall-arrest kit, Climbing gear
       │     • Cable: + Knee pads
       │
4. [HTTP] POST to Safety Equipment Ordering API
       │   URL: https://<safety-equipment-api>/api/orders
       │   Body: Compose output from step 3
       │   Retry policy: Exponential, 3 attempts
       │
5. [Response] Return 200 to caller
       │   Body:
       │   {
       │     "status": "SafetyEquipmentOrdered",
       │     "employeeId": "{employeeId}",
       │     "orderId": "{response from Safety Equipment API}",
       │     "message": "Safety equipment order placed for {employeeName} at {site}"
       │   }
```

---

## Key design points

| Aspect | Implementation |
| --- | --- |
| Trigger | HTTP POST — can be called by any HR system, webhook, or manual test |
| Routing | Single Condition action branches on `employeeType` |
| Downstream integration | HTTP POST actions trigger separate Device Registration and Safety Equipment APIs |
| Retry policy | Exponential backoff on downstream HTTP calls (3 attempts, 5s-30s intervals) |
| Error handling | If downstream API fails, return a 500 response with error details to the caller |
| Idempotency | `requestId` / `orderId` built from employeeId + timestamp prevents duplicate orders |
| Response | Synchronous — caller gets immediate confirmation with tracking ID |

---

## Demo walkthrough (presenter guide)

### Preparation

1. For the demo, use **https://httpbin.org/post** as a stand-in for both the Device Registration API and Safety Equipment Ordering API (it returns whatever you send it).

2. Pre-build the Logic App in the Azure portal (see "How to recreate the demo" section below).

3. Have the Logic App HTTP trigger URL ready to paste into PowerShell.

### Demo script

#### Part 1: Show the architecture (2 minutes)

- Explain the business problem: manual onboarding delays, missed equipment, compliance risk.
- Show the architecture diagram.
- Highlight: "One Logic App, one HTTP trigger, two flows based on employee type. No code."

#### Part 2: Demo Flow 1 — Office employee device registration (5 minutes)

1. Open the Logic App in the designer. Walk through each step visually.
2. Open PowerShell and trigger the workflow:

   ```powershell
   $url = "<paste-your-logic-app-trigger-url-here>"

   $officeEmployee = @{
       employeeId = "EMP-2026-0847"
       employeeName = "Sophie Martin"
       employeeType = "Office"
       department = "Network Planning"
       startDate = "2026-06-23"
       managerEmail = "manager@proximus.be"
       site = "Brussels HQ"
   } | ConvertTo-Json

   Invoke-RestMethod -Method Post -Uri $url -Body $officeEmployee -ContentType "application/json"
   ```

3. Show the response — it should contain `status: "DeviceRegistrationSubmitted"`.
4. Switch to the Azure portal and show the **run history** — open the completed run.
5. Walk through:
   - The trigger showing the incoming payload.
   - The Condition evaluating to True (Office path).
   - The Compose action showing the device registration payload.
   - The HTTP action showing the POST to the Device Registration API.
   - The Response action returning the result.

#### Part 3: Demo Flow 2 — Technician safety equipment order (5 minutes)

1. In PowerShell, trigger the workflow with a technician payload:

   ```powershell
   $technician = @{
       employeeId = "EMP-2026-0848"
       employeeName = "Thomas Dubois"
       employeeType = "Technician"
       department = "Field Operations"
       startDate = "2026-06-23"
       managerEmail = "sitemanager@proximus.be"
       site = "Brussels South"
       bootSize = 43
       clothingSize = "L"
       specialization = "Fiber"
   } | ConvertTo-Json

   Invoke-RestMethod -Method Post -Uri $url -Body $technician -ContentType "application/json"
   ```

2. Show the response — it should contain `status: "SafetyEquipmentOrdered"`.
3. Open the run history and walk through the Technician path.
4. Highlight the equipment payload sent to the Safety Equipment API (including boots, helmet, vest, glasses, harness for Fiber specialization).

#### Part 4: Show error handling (3 minutes)

1. Temporarily edit the Logic App and change the HTTP action URL to `https://httpbin.org/status/503`.
2. Trigger another employee request.
3. Show the run history — the HTTP action retries multiple times (visible in retry history).
4. Show the error response returned to the caller.
5. Restore the correct URL (`https://httpbin.org/post`).

#### Part 5: Show invalid input handling (2 minutes)

1. Send a request with an invalid `employeeType`:

   ```powershell
   $invalid = @{
       employeeId = "EMP-2026-0849"
       employeeName = "Invalid User"
       employeeType = "Contractor"
       department = "External"
       startDate = "2026-06-23"
   } | ConvertTo-Json

   Invoke-RestMethod -Method Post -Uri $url -Body $invalid -ContentType "application/json"
   ```

2. Show that the Condition evaluates to False for both branches.
3. The response returns a 400 with an "Unknown employee type" message.

#### Part 6: Key takeaways (2 minutes)

- **No code**: The entire workflow is visual and maintainable by operations teams.
- **HTTP-driven**: Any system can call it — HR platforms, ServiceNow, custom apps.
- **Downstream triggers**: The Logic App triggers other APIs for device registration and safety gear — enabling microservice-style orchestration.
- **Reliability**: Built-in retries, error handling, and full audit trail.
- **Compliance**: Every step is logged; run history provides the audit trail for safety compliance.
- **Scalability**: Consumption plan scales automatically — whether you onboard 1 or 100 employees that day.

---

## Connectors used

| Connector | Purpose |
| --- | --- |
| Request (built-in) | HTTP trigger — receives the employee onboarding payload |
| HTTP (built-in) | POST to Device Registration API and Safety Equipment Ordering API |
| Response (built-in) | Return result to the caller |
| Compose (Data Operations) | Build the downstream request payloads |
| Condition (Control) | Route based on employeeType |

---

## How to recreate the demo in Logic Apps (step-by-step)

Follow these detailed steps to build the demo Logic App from scratch in the Azure portal.

### Step 1: Create the Logic App resource

1. In the Azure portal, search for **Logic apps** and select it.
2. Select **+ Add**.
3. Configure:

   | Setting | Value |
   | --- | --- |
   | Subscription | Your subscription |
   | Resource group | `rg-logicapp-lab-<alias>` (or create a new one) |
   | Logic App name | `logic-employee-onboarding-<alias>` |
   | Region | `West Europe` |
   | Plan type | **Consumption** |

4. Select **Review + create** > **Create**.
5. Select **Go to resource**.

### Step 2: Add the HTTP Request trigger

1. In the Logic App overview, select **Logic app designer**.
2. Select **Blank Logic App**.
3. Search for **Request** and select **When a HTTP request is received**.
4. In the **Request Body JSON Schema** field, paste:

   ```json
   {
     "type": "object",
     "properties": {
       "employeeId": { "type": "string" },
       "employeeName": { "type": "string" },
       "employeeType": { "type": "string" },
       "department": { "type": "string" },
       "startDate": { "type": "string" },
       "managerEmail": { "type": "string" },
       "site": { "type": "string" },
       "bootSize": { "type": "integer" },
       "clothingSize": { "type": "string" },
       "specialization": { "type": "string" }
     },
     "required": ["employeeId", "employeeName", "employeeType", "department", "startDate"]
   }
   ```

5. Leave the method as **POST** (default).

### Step 3: Add the Condition action (route by employeeType)

6. Select **+ New step**.
7. Search for **Condition** and select **Condition** (Control).
8. Configure the condition:
   - Left value: select **Dynamic content** > **employeeType**
   - Operator: **is equal to**
   - Right value: `Office`

### Step 4: Build the "Office" branch (True side)

#### 4a. Add a Compose action to build the device registration payload

9. In the **True** branch, select **Add an action**.
10. Search for **Compose** and select **Compose** (Data Operations).
11. Rename it to `Build Device Registration Payload` (select **...** > **Rename**).
12. In the **Inputs** field, enter:

    ```json
    {
      "requestId": "DEV-@{triggerBody()?['employeeId']}-@{utcNow('yyyyMMddHHmmss')}",
      "employeeId": "@{triggerBody()?['employeeId']}",
      "employeeName": "@{triggerBody()?['employeeName']}",
      "department": "@{triggerBody()?['department']}",
      "deviceType": "Standard Laptop",
      "software": ["Microsoft 365", "Teams", "VPN Client", "Intune Enrollment"],
      "deliveryLocation": "@{triggerBody()?['site']}",
      "requiredBy": "@{triggerBody()?['startDate']}",
      "managerEmail": "@{triggerBody()?['managerEmail']}"
    }
    ```

#### 4b. Add an HTTP action to call the Device Registration API

13. After the Compose, select **Add an action**.
14. Search for **HTTP** and select the **HTTP** action (built-in).
15. Rename it to `POST to Device Registration API`.
16. Configure:

    | Setting | Value |
    | --- | --- |
    | Method | `POST` |
    | URI | `https://httpbin.org/post` |
    | Headers | Key = `Content-Type`, Value = `application/json` |
    | Body | Select **Dynamic content** > **Outputs** (from `Build Device Registration Payload`) |

17. Configure the retry policy:
    - Select **...** > **Settings**.
    - Under **Networking** > **Retry Policy**:
      - Type: **Exponential Interval**
      - Count: `3`
      - Minimum Interval: `PT5S`
      - Maximum Interval: `PT30S`
    - Select **Done**.

#### 4c. Add a Response action for the Office path

18. After the HTTP action, select **Add an action**.
19. Search for **Response** and select **Response**.
20. Rename it to `Response - Device Registration`.
21. Configure:

    | Setting | Value |
    | --- | --- |
    | Status Code | `200` |
    | Headers | Key = `Content-Type`, Value = `application/json` |
    | Body | (paste below) |

    Body:

    ```json
    {
      "status": "DeviceRegistrationSubmitted",
      "employeeId": "@{triggerBody()?['employeeId']}",
      "employeeName": "@{triggerBody()?['employeeName']}",
      "message": "Laptop registration request submitted for @{triggerBody()?['employeeName']}",
      "downstreamResponse": @{body('POST_to_Device_Registration_API')}
    }
    ```

### Step 5: Build the "Technician" branch (False side)

Since the Condition only checks for "Office", the False branch handles everything else. We'll add a nested condition to check for "Technician" and handle unknown types.

#### 5a. Add a nested Condition for Technician

22. In the **False** branch, select **Add an action**.
23. Search for **Condition** and select **Condition** (Control).
24. Configure:
    - Left value: select **Dynamic content** > **employeeType**
    - Operator: **is equal to**
    - Right value: `Technician`

#### 5b. Build the Technician True branch

##### Add a Compose action for the safety equipment payload

25. In the nested **True** branch (Technician), select **Add an action**.
26. Search for **Compose** and select **Compose**.
27. Rename it to `Build Safety Equipment Payload`.
28. In the **Inputs** field, enter:

    ```json
    {
      "orderId": "SAFETY-@{triggerBody()?['employeeId']}-@{utcNow('yyyyMMddHHmmss')}",
      "employeeId": "@{triggerBody()?['employeeId']}",
      "employeeName": "@{triggerBody()?['employeeName']}",
      "site": "@{triggerBody()?['site']}",
      "specialization": "@{triggerBody()?['specialization']}",
      "equipment": [
        { "item": "Safety Boots", "size": "@{triggerBody()?['bootSize']}" },
        { "item": "Safety Helmet", "size": "Universal" },
        { "item": "High-Visibility Vest", "size": "@{triggerBody()?['clothingSize']}" },
        { "item": "Safety Glasses", "size": "Universal" }
      ],
      "deliverTo": "@{triggerBody()?['site']}",
      "requiredBy": "@{triggerBody()?['startDate']}",
      "managerEmail": "@{triggerBody()?['managerEmail']}"
    }
    ```

##### Add an HTTP action to call the Safety Equipment Ordering API

29. After the Compose, select **Add an action**.
30. Search for **HTTP** and select the **HTTP** action.
31. Rename it to `POST to Safety Equipment API`.
32. Configure:

    | Setting | Value |
    | --- | --- |
    | Method | `POST` |
    | URI | `https://httpbin.org/post` |
    | Headers | Key = `Content-Type`, Value = `application/json` |
    | Body | Select **Dynamic content** > **Outputs** (from `Build Safety Equipment Payload`) |

33. Configure the retry policy:
    - Select **...** > **Settings**.
    - Retry Policy: **Exponential Interval**, Count: `3`, Min: `PT5S`, Max: `PT30S`.
    - Select **Done**.

##### Add a Response action for the Technician path

34. After the HTTP action, select **Add an action**.
35. Search for **Response** and select **Response**.
36. Rename it to `Response - Safety Equipment`.
37. Configure:

    | Setting | Value |
    | --- | --- |
    | Status Code | `200` |
    | Headers | Key = `Content-Type`, Value = `application/json` |
    | Body | (paste below) |

    Body:

    ```json
    {
      "status": "SafetyEquipmentOrdered",
      "employeeId": "@{triggerBody()?['employeeId']}",
      "employeeName": "@{triggerBody()?['employeeName']}",
      "site": "@{triggerBody()?['site']}",
      "message": "Safety equipment order placed for @{triggerBody()?['employeeName']} at @{triggerBody()?['site']}",
      "downstreamResponse": @{body('POST_to_Safety_Equipment_API')}
    }
    ```

#### 5c. Handle unknown employee types (nested False branch)

38. In the nested **False** branch (not Office, not Technician), select **Add an action**.
39. Search for **Response** and select **Response**.
40. Rename it to `Response - Unknown Type`.
41. Configure:

    | Setting | Value |
    | --- | --- |
    | Status Code | `400` |
    | Headers | Key = `Content-Type`, Value = `application/json` |
    | Body | (paste below) |

    Body:

    ```json
    {
      "status": "Error",
      "code": "UnknownEmployeeType",
      "message": "Employee type '@{triggerBody()?['employeeType']}' is not supported. Use 'Office' or 'Technician'.",
      "employeeId": "@{triggerBody()?['employeeId']}"
    }
    ```

### Step 6: Save and get the trigger URL

42. Select **Save** in the toolbar.
43. Select the **When a HTTP request is received** trigger.
44. Copy the **HTTP POST URL** that appears.
45. Save this URL — you will use it to test the demo.

### Step 7: Test the complete workflow

Open PowerShell and run the test calls:

**Test 1 — Office employee:**

```powershell
$url = "<paste-your-logic-app-trigger-url-here>"

$officeEmployee = @{
    employeeId = "EMP-2026-0847"
    employeeName = "Sophie Martin"
    employeeType = "Office"
    department = "Network Planning"
    startDate = "2026-06-23"
    managerEmail = "manager@proximus.be"
    site = "Brussels HQ"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri $url -Body $officeEmployee -ContentType "application/json"
```

Expected: Response with `"status": "DeviceRegistrationSubmitted"`.

**Test 2 — Technician employee:**

```powershell
$technician = @{
    employeeId = "EMP-2026-0848"
    employeeName = "Thomas Dubois"
    employeeType = "Technician"
    department = "Field Operations"
    startDate = "2026-06-23"
    managerEmail = "sitemanager@proximus.be"
    site = "Brussels South"
    bootSize = 43
    clothingSize = "L"
    specialization = "Fiber"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri $url -Body $technician -ContentType "application/json"
```

Expected: Response with `"status": "SafetyEquipmentOrdered"`.

**Test 3 — Unknown employee type:**

```powershell
$unknown = @{
    employeeId = "EMP-2026-0849"
    employeeName = "Invalid User"
    employeeType = "Contractor"
    department = "External"
    startDate = "2026-06-23"
} | ConvertTo-Json

try {
    Invoke-RestMethod -Method Post -Uri $url -Body $unknown -ContentType "application/json"
} catch {
    $_.Exception.Response.StatusCode
    $_.ErrorDetails.Message
}
```

Expected: HTTP 400 with `"code": "UnknownEmployeeType"`.

### Step 8: Verify in run history

1. In the Azure portal, open the Logic App.
2. Select **Overview** > **Run history**.
3. You should see 3 runs — open each and verify:
   - Run 1: Trigger → Condition (True/Office) → Compose → HTTP POST → 200 Response.
   - Run 2: Trigger → Condition (False) → Nested Condition (True/Technician) → Compose → HTTP POST → 200 Response.
   - Run 3: Trigger → Condition (False) → Nested Condition (False) → 400 Response.

---

## Extension ideas for the customer

- **Approval step**: Add a manager approval before high-cost equipment (e.g., Executive laptop or fall-arrest kit).
- **Adaptive Card in Teams**: Send an adaptive card to the manager to approve/reject equipment specs.
- **Email notifications**: Add Send Email actions after successful downstream calls to notify the manager and IT team.
- **Parallel branches**: For technicians, add a parallel branch that also schedules a safety induction (HTTP POST to a training system).
- **Error handling with Scopes**: Wrap the HTTP calls in a Try/Catch scope pattern (as shown in Lab 04) to send error notifications.
- **Monitoring dashboard**: Use a Power BI dashboard connected to Logic Apps run history to track onboarding SLAs.
- **Offboarding counterpart**: Create a reverse workflow that reclaims equipment and revokes access when an employee leaves.
