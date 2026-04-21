# Azentix IT Ops AI

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download)
[![Python 3.10+](https://img.shields.io/badge/Python-3.10+-blue.svg)](https://www.python.org/)
[![Docker](https://img.shields.io/badge/Docker-Supported-blue.svg)](https://www.docker.com/)

Purpose: Azentix is an enterprise operations copilot that turns plain-English requests into secure, auditable actions across ServiceNow, Stripe, SAP, Salesforce, and HubSpot.

What this solves:
- Gives support/ops teams one AI interface instead of five disconnected systems.
- Routes tasks to deterministic flows and plugins so operations stay predictable and low cost.
- Provides both chat UX and API/MCP interfaces for humans and AI clients.

## Integrated Use Cases (All Three)

1. SAP to Salesforce Price Sync
- Trigger: SAP price change event.
- Flow: Read latest SAP price, compare Salesforce pricebook, apply policy checks, sync or queue approval.
- Components: `price_sync_flow.py`, DirectorAgent rules, SAP/Salesforce plugins.

2. Incident Triage and Auto-Resolution
- Trigger: New or updated ServiceNow incident.
- Flow: Fetch incident, classify priority, search KB context, auto-resolve when safe, escalate when needed.
- Components: `incident_triage_flow.py`, ServiceNow plugin, RAG memory.

3. Stripe Billing Alert to ServiceNow Ticketing
- Trigger: Stripe payment failure/webhook.
- Flow: Validate webhook, enrich billing context, create/update ServiceNow incident, notify downstream systems.
- Components: `stripe_billing_flow.py`, `WebhookController.cs`, Stripe/ServiceNow plugins.

## Architecture

```text
User Chat UI (chat.html)
        |
        v
Kong Gateway (key-auth, rate-limit, CORS)
        |
        v
.NET Agent Host (Semantic Kernel DirectorAgent + MCP)
  |            |                 |
  |            |                 +--> MCP tools/list + tools/call + SSE stream
  |            +--> Plugins: ServiceNow, Stripe, SAP, Salesforce, HubSpot, RAG
  +--> HTTP APIs: /api/agents/execute, /api/agents/status, /health

CloudAMQP Queues
        |
        v
LangGraph Worker (Python)
  - price_sync_flow.py
  - incident_triage_flow.py
  - stripe_billing_flow.py
```

## Why Chat UI + LangGraph is Cost Efficient

- Intent routing is primarily keyword/deterministic before expensive reasoning.
- LangGraph nodes call APIs directly, so API orchestration itself consumes zero LLM tokens.
- Use `gpt-4o-mini` for reasoning to keep unit cost low.
- Use smaller context windows and task-specific prompts; avoid long history replay.

Practical estimate:
- Typical task (intent + reasoning + response): ~700-1500 tokens.
- At mini-model pricing, this is typically fractions of a cent per task.

## Local Run

```bash
cd src
dotnet restore
dotnet run --project Azentix.AgentHost/Azentix.AgentHost.csproj
```

Open:
- Chat app: `http://localhost:5000/chat.html`
- Dashboard: `http://localhost:5000/dashboard.html`
- MCP tools: `http://localhost:5000/mcp/tools`

## Docker Run

```bash
docker-compose -f docker/docker-compose.yml up -d
```

Open through gateway:
- Chat app: `http://localhost:8000/chat.html`
- Health: `http://localhost:8000/health`

## Render Deployment (API + Worker)

This repo includes `render.yaml` with two services:
- `azentix-itops-api` (web, Docker, .NET Agent Host)
- `azentix-langgraph-worker` (worker, Python LangGraph consumers)

Deploy steps:
1. Push this branch to GitHub.
2. In Render, create a Blueprint from repo root.
3. Render detects `render.yaml` and provisions both services.
4. Set all required secret env vars.

After deploy, your live chat URL is:
- `https://<your-web-service>.onrender.com/chat.html`

## Required Environment Variables

Core AI:
- `MODEL_PROVIDER` = `azure` or `ollama`
- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_API_KEY`
- `AZURE_OPENAI_CHAT_DEPLOYMENT` (recommend `gpt-4o-mini`)
- `AZURE_OPENAI_EMBED_DEPLOYMENT`

Security/Gateway:
- `INTERNAL_API_KEY`
- `CORS_ALLOWED_ORIGINS`
- `KONG_KEY_INTERNAL`
- `KONG_KEY_EXTERNAL`
- `KONG_KEY_LANGGRAPH`

Integrations:
- `SERVICENOW_INSTANCE_URL`, `SERVICENOW_USERNAME`, `SERVICENOW_PASSWORD`
- `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`
- `SAP_BASE_URL`, `SAP_API_KEY`
- `SALESFORCE_CLIENT_ID`, `SALESFORCE_CLIENT_SECRET`, `SALESFORCE_USERNAME`, `SALESFORCE_PASSWORD`
- `HUBSPOT_ACCESS_TOKEN`
- `SUPABASE_DB_CONNECTION`

Queue Worker:
- `CLOUDAMQP_URL`
- `RABBITMQ_QUEUE_SAP_PRICES` (default `sap-price-changes`)
- `RABBITMQ_QUEUE_INCIDENTS` (default `servicenow-incidents`)
- `RABBITMQ_QUEUE_STRIPE` (default `stripe-events`)

## How to Test End to End

1. API health
```bash
curl http://localhost:5000/health
```

2. Agent execution
```bash
curl -X POST http://localhost:5000/api/agents/execute \
  -H "Content-Type: application/json" \
  -H "X-API-Key: <your-key>" \
  -d '{"taskType":"incident-triage","description":"P1 outage in checkout","priority":2}'
```

3. MCP tool call
```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "X-API-Key: <your-key>" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

4. Chat UX
- Open `chat.html`.
- Ask in English, for example:
  - `Create a P2 incident for repeated Stripe payment failures.`
  - `Sync SAP material MAT-001234 price to Salesforce.`
  - `Triage incident INC0001234 and suggest next action.`

## Repository Highlights

- Agent host: `src/Azentix.AgentHost`
- Chat UI: `src/Azentix.AgentHost/wwwroot/chat.html`
- Dashboard: `src/Azentix.AgentHost/wwwroot/dashboard.html`
- MCP controller: `src/Azentix.AgentHost/Controllers/McpController.cs`
- App auth middleware: `src/Azentix.AgentHost/Middleware/ApiKeyAuthMiddleware.cs`
- LangGraph flows: `src/langgraph/flows`
- LangGraph worker: `src/langgraph/worker.py`
- Kong config: `kong/kong.yml`
- Render blueprint: `render.yaml`

## License

MIT
