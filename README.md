# Azentix — Enterprise Multi-Agent AI Orchestration Framework
> **100% Free Stack** | SAP · Salesforce · ServiceNow · HubSpot · Stripe
> Semantic Kernel · LangGraph · n8n · Kong · Supabase · CloudAMQP · Grafana Cloud
[![CI](https://github.com/SubhasisNanda0007/Azentix-Enterprise/actions/workflows/ci.yml/badge.svg)](https://github.com/SubhasisNanda/Azentix-Enterprise/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Stars](https://img.shields.io/github/stars/SubhasisNanda0007/Azentix-Enterprise?style=social)](https://github.com/SubhasisNanda/Azentix-Enterprise/stargazers)
-----
## Architecture
```
┌─────────────────────┐     ┌──────────────────────┐     ┌────────────────────────────────────────────────┐     ┌─────────────────────┐
│  ENTERPRISE SYSTEMS │     │   FREE MIDDLEWARE     │     │              AZENTIX AGENT CORE                │     │  DATA + OBSERV.     │
│                     │     │                       │     │         .NET 8 · Render.com · Docker           │     │                     │
│  ┌───────────────┐  │     │  ┌─────────────────┐ │     │                                                │     │  ┌───────────────┐  │
│  │ SAP S/4HANA   │  │────▶│  │  Kong Gateway   │ │     │  ┌──────────────────────────────────────────┐ │     │  │ Azure OpenAI  │  │
│  │ OData APIs    │  │     │  │  (APIM free alt)│ │────▶│  │            Director Agent                │ │────▶│  │ gpt-4o-mini   │  │
│  └───────────────┘  │     │  └─────────────────┘ │     │  │    ReAct · Semantic Kernel 1.21.0        │ │     │  │ text-emb-3-sm │  │
│                     │     │                       │     │  │  Thought → Plan → Act → Observe → Done  │ │     │  │ $200 credit   │  │
│  ┌───────────────┐  │     │  ┌─────────────────┐ │     │  └──────────────────────────────────────────┘ │     │  └───────────────┘  │
│  │  Salesforce   │  │────▶│  │      n8n        │ │     │                                                │     │                     │
│  │  CRM REST API │  │     │  │ (Logic Apps alt)│ │     │  ┌──────────┐  ┌────────────┐  ┌───────────┐ │     │  ┌───────────────┐  │
│  └───────────────┘  │     │  └─────────────────┘ │     │  │ RAG Agent│  │Memory Agent│  │ActionAgent│ │     │  │   Supabase    │  │
│                     │     │                       │     │  │ pgvector │  │Work+LongTrm│  │Retry+Fall │ │────▶│  │  pgvector     │  │
│  ┌───────────────┐  │     │  ┌─────────────────┐ │     │  └──────────┘  └────────────┘  └───────────┘ │     │  │ HNSW index    │  │
│  │  ServiceNow   │  │────▶│  │  CloudAMQP      │ │     │                                                │     │  │ 500MB free    │  │
│  │  Table API    │  │     │  │  RabbitMQ       │ │     │  ── SEMANTIC KERNEL PLUGINS ──────────────── │     │  └───────────────┘  │
│  └───────────────┘  │     │  │ (SvcBus free alt│ │     │  ┌──────┐ ┌───────────┐ ┌───────────┐       │     │                     │
│                     │     │  └─────────────────┘ │     │  │ SAP  │ │Salesforce │ │ServiceNow │       │     │  ┌───────────────┐  │
│  ┌───────────────┐  │     │                       │     │  │4 fns │ │  5 fns    │ │  4 fns    │       │     │  │Grafana Cloud  │  │
│  │   HubSpot     │  │────▶│  QUEUES               │     │  └──────┘ └───────────┘ └───────────┘       │     │  │ Loki · Prom   │  │
│  │  CRM API      │  │     │  sap-price-changes    │     │  ┌──────┐ ┌───────────┐ ┌───────────┐       │     │  │ OTel traces   │  │
│  └───────────────┘  │     │  servicenow-incidents │     │  │HubSpt│ │  Stripe   │ │ RabbitMQ  │       │     │  │ 50GB free     │  │
│                     │     │  stripe-events        │     │  │5 fns │ │  4 fns    │ │ publisher │       │     │  └───────────────┘  │
│  ┌───────────────┐  │     │  approval-queue       │     │  └──────┘ └───────────┘ └───────────┘       │     │                     │
│  │    Stripe     │  │────▶│  notifications        │     │                                                │     │  ┌───────────────┐  │
│  │  Payments API │  │     │  dead-letter-archive  │     │  ── LANGGRAPH FLOWS (Python) ─────────────── │     │  │   Doppler     │  │
│  └───────────────┘  │     │                       │     │  price_sync_flow.py  · SAP→Salesforce sync   │     │  │ (KeyVault alt)│  │
│                     │     │  ┌─────────────────┐ │     │  incident_triage_flow.py · ServiceNow P1-P4  │     │  │ 3 proj free   │  │
│  All have free      │     │  │    Doppler      │ │     │  stripe_billing_flow.py · Failed payments     │     │  └───────────────┘  │
│  developer tiers    │     │  │ (Key Vault alt) │ │     │  lead_enrichment_flow.py · SAP→SF enrich      │     │                     │
│                     │     │  └─────────────────┘ │     │  hubspot_sync_flow.py · SF→HubSpot realtime  │     │  ┌───────────────┐  │
└─────────────────────┘     └──────────────────────┘     │                                                │     │  GitHub Actions│  │
                                                         │  ── CI/CD ──────────────────────────────────  │     │  CI + Deploy   │  │
                                                         │  Build · Test · Kong validate · Docker         │     │  Render.com    │  │
                                                         │  Trivy CVE scan · TruffleHog secrets check    │     │  host          │  │
                                                         └────────────────────────────────────────────────┘     └─────────────────────┘
Only Azure OpenAI is paid ($200 free credit ≈ 6 months). Everything else: £0/month forever.
```
-----
## What Is Azentix?
Azentix is a production-ready multi-agent AI framework that connects to the systems enterprises actually run. AI agents reason using a ReAct loop, call enterprise systems as tools, and complete full business workflows — automatically, in seconds, at zero recurring cost.
**Only Azure OpenAI is paid.** Everything else runs on free tiers that never expire.
-----
## The 5 Use Cases
|Use Case                       |Problem Solved                                                                 |Time to Fix      |
|-------------------------------|-------------------------------------------------------------------------------|-----------------|
|**SAP → Salesforce Price Sync**|Prices change in SAP but Salesforce stays stale. Sales reps quote wrong prices.|8 seconds        |
|**ServiceNow Incident Triage** |P1 incidents sit in New for 45 minutes. Wrong team assigned. Alert fatigue.    |60 seconds       |
|**Salesforce Lead Enrichment** |Sales reps spend 2 hours enriching leads. CRM data is 60% complete.            |Instant on create|
|**HubSpot Contact Sync**       |HubSpot marketing lists out of sync with Salesforce CRM.                       |Real-time        |
|**Stripe Billing Alerts**      |Failed payments go unnoticed. Customers churn silently.                        |Instant detection|
-----
## Free Stack — What Replaces What
|Azure Service             |Free Replacement             |Free Tier Limit              |
|--------------------------|-----------------------------|-----------------------------|
|Azure Service Bus         |**CloudAMQP RabbitMQ**       |1M messages/month, forever   |
|Azure Logic Apps          |**n8n** self-hosted on Render|Unlimited workflows          |
|Azure APIM                |**Kong Gateway** open source |Unlimited, MIT license       |
|Azure App Service         |**Render.com**               |1 service, 512MB RAM         |
|Azure Key Vault           |**Doppler**                  |3 projects, unlimited secrets|
|Azure Application Insights|**Grafana Cloud**            |50GB logs, 10K metrics/month |
|Azure Cosmos DB           |**Supabase pgvector**        |500MB + unlimited requests   |
|Azure Bicep IaC           |**Docker Compose**           |Free, runs anywhere          |
-----
## Tech Stack
```
AI Model            Azure OpenAI gpt-4o-mini
AI Embeddings       Azure OpenAI text-embedding-3-small
Agent Framework     Semantic Kernel 1.21.0 (.NET 8)
Agent Flows         LangGraph 0.2.28 (Python 3.11)
Vector Memory       Supabase pgvector + HNSW index
Message Broker      CloudAMQP RabbitMQ (Little Lemur plan)
Workflow Engine     n8n self-hosted
API Gateway         Kong Gateway declarative config
App Hosting         Render.com free tier
Secrets Manager     Doppler
Observability       Grafana Cloud + OpenTelemetry
CI/CD               GitHub Actions (2000 min/month free)
Dev Environment     GitHub Codespaces (60hr/month free)
```
-----
## Quickstart — First Agent Run in 30 Minutes
### Step 1 — Set up all 13 accounts (Day 1, ~3 hours)
Complete ALL accounts before writing any code.
|Service       |URL                                       |What You Need                       |
|--------------|------------------------------------------|------------------------------------|
|Azure OpenAI  |https://azure.microsoft.com/free          |Endpoint + API key                  |
|Supabase      |https://supabase.com                      |Project URL + anon + service keys   |
|CloudAMQP     |https://www.cloudamqp.com                 |AMQP URL — Little Lemur plan        |
|n8n           |https://render.com                        |Deploy docker image n8nio/n8n:latest|
|Render.com    |https://render.com                        |Account for app hosting             |
|Doppler       |https://doppler.com                       |Secrets project                     |
|Grafana Cloud |https://grafana.com/auth/sign-up          |Prometheus + Loki endpoints         |
|SAP API Hub   |https://api.sap.com                       |API key — instant, no account needed|
|Salesforce Dev|https://developer.salesforce.com/signup   |Connected app credentials           |
|ServiceNow PDI|https://developer.servicenow.com          |Instance URL + admin password       |
|HubSpot Free  |https://app.hubspot.com/signup-hubspot/crm|Private app access token            |
|Stripe Test   |https://dashboard.stripe.com/register     |sk_test + webhook secret            |
### Step 2 — Clone and configure environment
```bash
git clone https://github.com/Subhasis0007/Azentix-Enterprise.git
cd Azentix-Enterprise
cp .env.example .env
# Fill in every value in .env
```
### Step 3 — Set up Supabase schema
In the Supabase SQL Editor, run `docs/setup/supabase-schema.sql`.
Creates: `agent_memory` table, HNSW index, `match_documents()` vector search function.
### Step 4 — Create CloudAMQP queues
```bash
pip install pika
python scripts/setup_rabbitmq.py
```
Creates: `sap-price-changes`, `servicenow-incidents`, `stripe-events`, `hubspot-sync`, `approval-queue`, `notifications`, `dead-letter-archive`
### Step 5 — Start local infrastructure
```bash
docker-compose up -d kong n8n
```
- Kong proxy: http://localhost:8000
- Kong admin: http://localhost:8001
- n8n UI: http://localhost:5678
### Step 6 — Apply Kong configuration
```bash
# Mac
brew install deck
# Linux
curl -sL https://github.com/Kong/deck/releases/latest/download/deck_linux_amd64.tar.gz | tar -xz
sudo mv deck /usr/local/bin/
# Apply config
deck sync --kong-addr http://localhost:8001 --state kong/kong.yml
# Output: Created 2 services, 2 routes, 6 plugins, 3 consumers
```
### Step 7 — Build and run
```bash
pip install -r src/langgraph/requirements.txt
dotnet build src/Azentix.sln
# With Doppler
doppler run -- dotnet run --project src/Azentix.AgentHost
# Without Doppler
dotnet run --project src/Azentix.AgentHost
```
Health check: http://localhost:5000/health
### Step 8 — Fire your first agent
```bash
curl -X POST http://localhost:8000/azentix/v1/agents/execute \
 -H "Content-Type: application/json" \
 -H "X-API-Key: azentix-internal-key-change-this" \
 -d '{
   "taskType": "sap-salesforce-price-sync",
   "description": "Sync SAP price for MAT-001234 to Salesforce",
   "priority": "High",
   "inputData": {
     "sapMaterialNumber": "MAT-001234",
     "salesforceProductId": "01t5e000003K9XAAA0",
     "triggeredBy": "manual_test"
   }
 }'
```
Expected response:
```json
{
 "taskId": "...",
 "status": "Completed",
 "finalAnswer": "Salesforce updated. Ref SYNC-20241115-MAT001234. Discrepancy 6.2% resolved.",
 "totalIterations": 3,
 "totalTokensUsed": 1987,
 "duration": "00:00:08.2"
}
```
### Step 9 — Import n8n workflows
1. Open n8n at http://localhost:5678
1. Workflows → Import from File
1. Import each JSON from `n8n-workflows/`
1. Add CloudAMQP credential (Settings → Credentials → RabbitMQ → AMQP URL)
1. Activate each workflow (green toggle)
### Step 10 — Test LangGraph flows directly
```bash
python src/langgraph/flows/price_sync_flow.py
```
Expected output:
```
Status:  synced
SAP Price:  GBP 249.99
SF Price:   234.5
Discrepancy: 6.2%
Sync Ref:  SYNC-20241115-MAT001234
Audit Trail:
[08:00:01] validate       -> Material MAT-001234
[08:00:02] fetch_sap      -> SAP: GBP 249.99
[08:00:03] fetch_sf       -> SF: 234.50
[08:00:04] rag_rules      -> 3 rules from Supabase
[08:00:05] analyse        -> Diff:15.49 (6.2%) -> finance_manager
[08:00:06] auto_sync      -> Salesforce updated. Ref:SYNC-...
[08:00:07] notify         -> Status:synced
```
-----
## Project Structure
```
Azentix-Enterprise/
├── .devcontainer/
│   ├── devcontainer.json           # GitHub Codespaces auto-setup
│   └── setup.sh                    # Installs all tools on container start
├── .github/
│   └── workflows/
│       ├── ci.yml                  # Build · Test · Kong validate · Docker · Security
│       └── deploy.yml              # Deploy to Render.com on push to main
├── src/
│   ├── Azentix.AgentHost/
│   │   ├── Controllers/
│   │   │   ├── AgentController.cs  # POST /api/agents/execute
│   │   │   ├── HealthController.cs # GET /health
│   │   │   └── WebhookController.cs# POST /api/webhooks/stripe
│   │   ├── Program.cs              # DI registration for all agents and plugins
│   │   ├── Dockerfile              # Used by Render.com and local Docker
│   │   └── appsettings.json
│   ├── Azentix.Agents/
│   │   ├── Director/
│   │   │   ├── DirectorAgent.cs    # ReAct orchestrator — the brain
│   │   │   └── IDirectorAgent.cs
│   │   ├── Rag/
│   │   │   ├── RagAgent.cs         # pgvector semantic search
│   │   │   └── DocumentIngestionService.cs
│   │   ├── Memory/
│   │   │   └── SupabaseVectorMemory.cs  # Npgsql + pgvector operations
│   │   ├── Action/
│   │   │   └── ActionAgent.cs      # Tool execution with retry logic
│   │   └── Plugins/
│   │       ├── SapPlugin.cs        # 4 KernelFunctions
│   │       ├── SalesforcePlugin.cs # 5 KernelFunctions
│   │       ├── ServiceNowPlugin.cs # 4 KernelFunctions
│   │       ├── HubSpotPlugin.cs    # 5 KernelFunctions
│   │       ├── StripePlugin.cs     # 4 KernelFunctions
│   │       ├── RabbitMQPlugin.cs   # CloudAMQP publisher
│   │       └── RagPlugin.cs        # Exposes RAG as SK plugin
│   ├── Azentix.Models/
│   │   ├── AgentTask.cs            # Input shape — sent by n8n
│   │   ├── AgentResult.cs          # Output shape — read by n8n
│   │   └── *Configuration.cs       # One config record per system
│   ├── Azentix.Tests.Unit/         # xUnit — 20+ tests minimum
│   └── langgraph/
│       ├── flows/
│       │   ├── price_sync_flow.py
│       │   ├── incident_triage_flow.py
│       │   ├── stripe_billing_flow.py
│       │   ├── lead_enrichment_flow.py
│       │   └── hubspot_sync_flow.py
│       ├── nodes/                  # Shared node functions
│       ├── states/                 # TypedDict state definitions
│       └── requirements.txt
├── docker/
│   └── docker-compose.yml          # Kong + n8n + RabbitMQ (local) + Grafana Agent
├── kong/
│   └── kong.yml                    # Declarative config — routes, plugins, consumers, rate limits
├── n8n-workflows/
│   ├── sap-price-sync.json
│   ├── incident-triage.json
│   └── stripe-billing-alert.json
├── grafana/
│   ├── dashboards/
│   │   ├── azentix-overview.json   # Agent success rate, latency, token usage
│   │   └── token-budget.json       # Per-tenant token consumption
│   └── agent-config.yaml           # Ships logs and metrics to Grafana Cloud
├── scripts/
│   ├── ingest_knowledge.py         # Seeds Supabase with SAP/ServiceNow KB
│   ├── test_connections.py         # Tests all 7 systems
│   └── setup_rabbitmq.py           # Creates all queues on CloudAMQP
├── samples/                        # Example JSON payloads per use case
├── docs/
│   └── setup/
│       └── supabase-schema.sql     # Run this in Supabase SQL editor on Day 1
├── .env.example                    # All required variables — commit this, never .env
├── .gitignore                      # .env, bin, obj, node_modules excluded
├── LICENSE                         # MIT
└── README.md
```
-----
## Deployment
### Render.com (free hosting)
1. New → Web Service → connect `Azentix-Enterprise` repo
1. Runtime: Docker
1. Dockerfile path: `src/Azentix.AgentHost/Dockerfile`
1. Instance type: Free
1. Health check path: `/health`
1. Connect Doppler → Render integration for automatic secret sync
> Free tier spins down after 15 minutes idle. Use cron-job.org">https://cron-job.org to ping `/health` every 14 minutes to keep it warm.
### Doppler secrets injection
```bash
# Install
curl -Ls https://cli.doppler.com/install.sh | sh
# Link to project
doppler login
doppler setup   # select: azentix-enterprise -> dev
# Run locally with all secrets
doppler run -- dotnet run --project src/Azentix.AgentHost
```
Connect Doppler → Render and Doppler → GitHub Actions via the Doppler integrations dashboard to auto-sync all secrets.
-----
## Observability
```bash
docker-compose up -d grafana-agent
```
Import these into Grafana Cloud → Dashboards → Import:
- `grafana/dashboards/azentix-overview.json`
- `grafana/dashboards/token-budget.json`
Useful queries:
```logql
# Agent success rate (Loki)
{app="azentix"} |= "DirectorAgent END" | json | line_format "{{.status}}"
# Kong rate limit hits (Prometheus)
kong_http_requests_total{service="azentix-agent-service",status="429"}
# RabbitMQ queue depth
rabbitmq_queue_messages{queue="sap-price-changes"}
```
-----
## Running Tests
```bash
# .NET unit tests
dotnet test src/Azentix.sln --filter "Category=Unit"
# Python tests (mocked, no live systems needed)
python -m pytest tests/python/ -v -m "not integration"
# Verify all 7 system connections
python scripts/test_connections.py
# Validate Kong config syntax
deck validate --state kong/kong.yml
```
-----
## CI/CD Pipeline
Every push to `main` triggers five jobs:
|Job          |What it does                                                |
|-------------|------------------------------------------------------------|
|build-dotnet |`dotnet build` + xUnit tests + Codecov coverage             |
|test-python  |pytest against mocked systems                               |
|validate-kong|`deck validate` — no Kong instance needed                   |
|docker-build |Full Docker image build verification                        |
|security-scan|Trivy CVE scan + TruffleHog secrets + `.env` committed check|
Secrets flow: Doppler → GitHub Secrets (auto-sync) → Render deploy on pass.
-----
## Environment Variables
See `.env.example` for the full list. Key groups:
|Group       |Key Variables                                                                                                          |
|------------|-----------------------------------------------------------------------------------------------------------------------|
|Azure OpenAI|`AZURE_OPENAI_ENDPOINT` `AZURE_OPENAI_API_KEY` `AZURE_OPENAI_DEPLOYMENT_NAME` `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`      |
|Supabase    |`SUPABASE_URL` `SUPABASE_ANON_KEY` `SUPABASE_SERVICE_KEY` `SUPABASE_DB_CONNECTION`                                     |
|CloudAMQP   |`CLOUDAMQP_URL` `RABBITMQ_QUEUE_SAP_PRICES` `RABBITMQ_QUEUE_INCIDENTS` `RABBITMQ_QUEUE_STRIPE`                         |
|n8n         |`N8N_URL` `N8N_WEBHOOK_BASE` `N8N_BASIC_AUTH_USER` `N8N_BASIC_AUTH_PASSWORD`                                           |
|Kong        |`KONG_PROXY_URL` `KONG_ADMIN_URL` `KONG_API_KEY_AZENTIX`                                                               |
|SAP         |`SAP_BASE_URL` `SAP_API_KEY` `SAP_SYSTEM` `SAP_DEFAULT_SALES_ORG`                                                      |
|Salesforce  |`SALESFORCE_INSTANCE_URL` `SALESFORCE_CLIENT_ID` `SALESFORCE_CLIENT_SECRET` `SALESFORCE_USERNAME` `SALESFORCE_PASSWORD`|
|ServiceNow  |`SERVICENOW_INSTANCE_URL` `SERVICENOW_USERNAME` `SERVICENOW_PASSWORD`                                                  |
|HubSpot     |`HUBSPOT_ACCESS_TOKEN` `HUBSPOT_PORTAL_ID` `HUBSPOT_API_BASE`                                                          |
|Stripe      |`STRIPE_SECRET_KEY` `STRIPE_WEBHOOK_SECRET` `STRIPE_API_VERSION`                                                       |
|Grafana     |`GRAFANA_PROMETHEUS_URL` `GRAFANA_PROMETHEUS_USER` `GRAFANA_API_KEY` `GRAFANA_LOKI_URL`                                |
-----
## Troubleshooting
|Problem                                |Fix                                                                                              |
|---------------------------------------|-------------------------------------------------------------------------------------------------|
|Supabase `connection refused`          |Use port **5432** not 6543 — get direct connection string from Dashboard → Settings → Database   |
|CloudAMQP `connection refused`         |Free tier: 1 concurrent connection. Close all previous connections before opening a new one      |
|n8n not triggering from queue          |Check workflow is **Activated** (green toggle). Verify AMQP URL credential is set in n8n Settings|
|Kong returns 401                       |`X-API-Key` header must exactly match `keyauth_credentials.key` in `kong/kong.yml`               |
|Render cold start (30s delay)          |Ping `/health` every 14 minutes via cron-job.org to keep the service warm                        |
|ServiceNow 403 Forbidden               |PDI user needs `itil` role: User Admin → Users → your user → Roles tab                           |
|Salesforce `INVALID_CLIENT_CREDENTIALS`|Wait 10 minutes after creating Connected App before using the credentials                        |
|SAP 429 Too Many Requests              |Sandbox limit: 20 req/min. Add `time.sleep(3)` between calls in batch scripts                    |
|LangGraph JSON parse error             |LLM returned explanation before JSON — the `except` fallback in `analyse_node` handles this      |
|ServiceNow PDI not responding          |Instance hibernates after 10 days idle. Go to developer.servicenow.com → My Instance → Wake Up   |
-----
## Month-by-Month Build Plan
|Month|Focus                            |Success Criteria                                                           |
|-----|---------------------------------|---------------------------------------------------------------------------|
|1    |Accounts + repo setup            |All 13 services configured. `python scripts/test_connections.py` passes    |
|2    |Core agents                      |`dotnet build` with 0 errors. `/health` returns 200                        |
|3    |5 plugins + LangGraph flows      |`python price_sync_flow.py` shows full audit trail                         |
|4    |n8n + Kong + Docker Compose      |End-to-end: RabbitMQ → n8n → Kong → Agent → Salesforce updated             |
|5    |Program.cs + Dockerfile + Doppler|Deployed to Render. All health checks green                                |
|6    |CI/CD + Grafana + Launch         |All 5 CI jobs green. Dashboard live. v1.0.0 tagged. LinkedIn post published|
-----
## Cost Summary
|Component                                                     |Monthly Cost                                        |
|--------------------------------------------------------------|----------------------------------------------------|
|Azure OpenAI gpt-4o-mini                                      |~$10–30/month after $200 free credit (~6 months dev)|
|CloudAMQP + n8n + Kong + Render + Doppler + Grafana + Supabase|**£0 / $0**                                         |
|**Total**                                                     |**~$10–30/month**                                   |
Azure-native equivalent stack would cost: **£200–500/month**
-----
## Contributing
1. Fork the repo
1. `git checkout -b feat/your-feature`
1. Commit with conventional format: `feat: add X` / `fix: resolve Y` / `docs: update Z`
1. Open a PR against `main` — all 5 CI jobs must pass
-----
## Author
**Subhasis Nanda** — Senior Azure AI Integration Engineer, Capgemini, Bengaluru
The patterns in this framework come from real enterprise engagements: SAP + Salesforce price integrations at manufacturing clients, ServiceNow ITSM triage automation, and Stripe billing pipelines where silent failures were costing revenue.
-----
## License
MIT — see <LICENSE>
*“The difference between a senior engineer and an MVP is not the title. It is the decision to share what you know with everyone who needs it.”*