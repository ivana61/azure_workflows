# Azure Workflows in the Enterprise – Design, Security & Operations

## Target Audience

- Senior IT engineers, M365 admins, solution architects
- Strong O365 / Entra ID / Graph knowledge
## Workshop Goals

- Understand when and why to use Azure Workflows
- Design secure, scalable, auditable workflows
- Learn real production patterns, not demo flows
- Build a common foundation for an internal Azure Workflow competence centre

---

## Day 1 – Foundations & Core Building Blocks

### Introduction & Positioning

- What we mean by "Azure Workflows"
- Where Azure Workflows sit compared to:
  - Power Automate
  - On prem scripts
  - Custom apps
- Typical enterprise use cases (provisioning, lifecycle, integration)

**Goal:** Align understanding and set expectations

---

### Azure Workflow Landscape (Architecture View)

#### Conceptual Overview

- Azure Logic Apps
- Azure Functions
- Azure Automation (Runbooks)
- Azure Data Factory (briefly – orchestration use cases)
- Event vs scheduled vs request driven workflows

#### Decision Matrix

- Which tool for which scenario
- Cost, complexity, maintainability trade offs

---

### Identity & Security Fundamentals for Workflows

#### Critical Topics

- Managed Identities (why passwords are not acceptable)
- Service Principals and least privilege design
- Microsoft Graph permissions strategy
- Key Vault patterns
- Credential rotation and auditability

#### Common Mistakes

- Over privileged app registrations
- Hardcoded secrets
- "It works, so ship it" security decisions

---

### Logic Apps Deep Dive (Without Basics)

#### Expert Focus

- Consumption vs Standard
- Triggers & connectors at scale
- Error handling patterns
- Idempotency and retry logic
- Throttling and Graph limitations

#### Hands On (Guided)

- Build a secure request based Logic App
- Use Managed Identity + Graph call
- Implement structured error handling

---

### Azure Functions for Workflow Logic

#### Topics

- When Logic Apps are not enough
- Function triggers (HTTP, timer, events)
- Durable Functions for stateful workflows
- Logging with App Insights

#### Design Principle

Logic Apps orchestrate – Functions encapsulate logic

---

## Day 2 – Enterprise Scenarios, Governance & Operations

### Enterprise Use Cases (Real World)

#### Typical Workflows

- User lifecycle automation
- Teams / SharePoint provisioning
- MFA reset & recovery flows
- Cross tenant operations
- ServiceNow triggered workflows

These patterns are directly aligned with scenarios referenced in internal Azure Workflow and automation material.

---

### Integration Patterns

#### Topics

- REST & Graph API design
- Webhooks vs polling
- Event driven architecture
- Parallelism & batching

#### Hands On

- Trigger a workflow from an external system
- Validate input & handle failures cleanly

---

### Monitoring, Logging & Troubleshooting

#### Operational Reality

- Application Insights: what to log and why
- Centralised logging strategy
- Alerting on failures
- Debugging production issues

#### Key Message

No monitoring = no enterprise grade workflow

---

### Governance, Standards & Lifecycle

#### Important Aspects

- Naming conventions
- Versioning workflows
- Environment separation (dev / test / prod)
- Access control model
- Documentation & handover

#### Discussion

- How to avoid "shadow automation"
- Building reusable templates

---

### CI/CD & Automation Maturity

#### Topics

- Infrastructure as Code (Bicep/ARM – conceptually)
- Git based deployment
- Secure promotion between environments
- Rollback strategies

Aligned with internal cloud automation and CI/CD thinking visible in related presentations.

---

### Hybrid Reality in Enterprises

#### Talking Points

- Hybrid is still the norm (not an exception)
- Cloud workflows must coexist with:
  - On prem AD
  - Legacy systems
  - Fileshares / legacy apps

---

## Deliverables You Can Base This On

- Architecture & decision diagrams
- Security checklist for Azure Workflows
- Standard workflow template
- Logging & monitoring baseline
- Enterprise use case catalogue
