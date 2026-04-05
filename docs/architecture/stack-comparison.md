# Azentix — Free Stack vs Azure-Native Stack

## Cost Comparison

| Azure Service | Typical Cost/month | Free Replacement | Free Tier |
|---|---|---|---|
| Azure Service Bus | £10–50 | CloudAMQP RabbitMQ | 1M messages/month forever |
| Azure Logic Apps | pay-per-exec | n8n (self-hosted) | Unlimited workflows |
| Azure APIM | £25–100 | Kong Gateway | Open source, unlimited |
| Azure App Service | £50–200 | Render.com | 750 hrs/month, free tier |
| Azure Key Vault | £5–20 | Doppler | 3 projects forever |
| Azure App Insights | £0–30 | Grafana Cloud | 50GB logs/month |
| Azure Bicep/ARM | £0 | Docker Compose | Free |
| Azure Cosmos DB | £20–100 | Supabase pgvector | 500MB forever |
| **Total Azure** | **£110–500/month** | **Total Free** | **£0/month** |

Only Azure OpenAI is paid. $200 free credit ≈ 6 months of development.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                   ENTERPRISE SYSTEMS                         │
│   SAP S/4HANA    Salesforce    ServiceNow    HubSpot  Stripe │
└──────────────────────┬───────────────────────────────────────┘
                       │  Events / Webhooks
┌──────────────────────▼───────────────────────────────────────┐
│         CloudAMQP RabbitMQ  (replaces Azure Service Bus)      │
│   sap-price-changes │ servicenow-incidents │ stripe-events    │
│   hubspot-sync      │ approval-queue       │ notifications    │
└──────────────────────┬───────────────────────────────────────┘
                       │  Queue triggers
┌──────────────────────▼───────────────────────────────────────┐
│              n8n  (replaces Azure Logic Apps)                  │
│   SAP Price Sync │ Incident Triage │ Stripe Alert             │
└──────────────────────┬───────────────────────────────────────┘
                       │  HTTP POST  (X-API-Key auth)
┌──────────────────────▼───────────────────────────────────────┐
│            Kong Gateway  (replaces Azure APIM)                 │
│   Rate Limit: 60/min │ Cache: 5min │ Auth │ CORS │ Logs       │
└──────────────────────┬───────────────────────────────────────┘
                       │  Proxy → Render.com
┌──────────────────────▼───────────────────────────────────────┐
│         Azentix Agent Host  (.NET 8  on Render.com)            │
│   DirectorAgent (Semantic Kernel ReAct — gpt-4o-mini)          │
│                                                                │
│   Plugins:                                                     │
│   SapPlugin │ SalesforcePlugin │ ServiceNowPlugin              │
│   HubSpotPlugin │ StripePlugin │ RabbitMQPlugin │ RagPlugin    │
└──────────────────────┬───────────────────────────────────────┘
                       │  pgvector queries
┌──────────────────────▼───────────────────────────────────────┐
│          Supabase pgvector  (replaces Azure Cosmos DB)         │
│   agent_memory table │ HNSW index │ match_documents() fn      │
│   Collections: sap-salesforce-sync │ servicenow-kb            │
│                stripe-policies │ default                       │
└──────────────────────────────────────────────────────────────┘
```

## LangGraph Flow Architecture

```
price_sync_flow.py:
  validate → fetch_sap → fetch_sf → rag_rules → analyse
    ↓ sync_required=false          ↓ approval_needed=true
  notify ←─────────── auto_sync ←─ approval_queue

incident_triage_flow.py:
  validate → fetch_incident → search_kb → classify
    ↓ confidence≥0.8 + auto_resolvable    ↓ escalate
  auto_resolve ──────────────────────────→ END

stripe_billing_flow.py:
  validate → fetch_stripe → fetch_customer → create_snow_incident
    → flag_salesforce → update_hubspot → notify
```
