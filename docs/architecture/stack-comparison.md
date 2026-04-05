# Azentix Free Stack vs Azure-Native Stack

## Service Replacements

| Azure Service | Cost/month | Free Replacement | Free Tier |
|--------------|-----------|-----------------|-----------|
| Azure Service Bus | £10-50 | CloudAMQP RabbitMQ | 1M messages/month forever |
| Azure Logic Apps | pay-per-exec | n8n (self-hosted) | Unlimited workflows |
| Azure APIM | £25-100 | Kong Gateway | Open source, unlimited |
| Azure App Service | £50-200 | Render.com | 750 hrs/month |
| Azure Key Vault | £5-20 | Doppler | 3 projects forever |
| Azure App Insights | £0-30 | Grafana Cloud | 50GB logs/month |
| Azure Bicep/ARM | £0 | Docker Compose | Free |
| Azure Cosmos DB | £20-100 | Supabase pgvector | 500MB forever |
| **Total** | **£110-500** | | **£0/month** |

Only Azure OpenAI is paid. $200 free credit ≈ 6 months development.

## Architecture Diagram

```
                    ┌──────────────────────────────────────────────┐
                    │           ENTERPRISE SYSTEMS                  │
                    │  SAP S/4HANA   Salesforce   ServiceNow       │
                    │  HubSpot       Stripe                         │
                    └──────────────┬───────────────────────────────┘
                                   │ Events / Webhooks
                    ┌──────────────▼───────────────────────────────┐
                    │   CloudAMQP RabbitMQ  (replaces Service Bus)  │
                    │   sap-prices | incidents | stripe | hubspot   │
                    └──────────────┬───────────────────────────────┘
                                   │ Queue trigger
                    ┌──────────────▼───────────────────────────────┐
                    │     n8n Workflows  (replaces Logic Apps)       │
                    └──────────────┬───────────────────────────────┘
                                   │ HTTP POST
                    ┌──────────────▼───────────────────────────────┐
                    │     Kong Gateway   (replaces Azure APIM)       │
                    │     Auth | Rate Limit | Cache | Logs           │
                    └──────────────┬───────────────────────────────┘
                                   │ Proxy
                    ┌──────────────▼───────────────────────────────┐
                    │   Azentix Agent Host  (.NET 8 on Render.com)   │
                    │   DirectorAgent  (Semantic Kernel ReAct)        │
                    │   Plugins: SAP | SF | SNOW | HS | Stripe | MQ  │
                    └──────────────┬───────────────────────────────┘
                                   │ pgvector queries
                    ┌──────────────▼───────────────────────────────┐
                    │   Supabase pgvector  (replaces Cosmos DB)      │
                    │   Vector memory + RAG knowledge base            │
                    └──────────────────────────────────────────────┘
```
