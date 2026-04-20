# Azentix Enterprise

Enterprise multi-agent orchestration for SAP, Salesforce, ServiceNow, HubSpot, and Stripe.

This repository now supports two model backends with one codebase:

- Online mode: Azure OpenAI / Microsoft Foundry deployment
- Offline mode: Local Ollama model (for example `qwen3:8b`)

You can switch modes with one environment variable:

```bash
MODEL_PROVIDER=azure   # online
MODEL_PROVIDER=ollama  # offline
```

## What Was Added

- Runtime provider switch in the .NET host and agent runtime
- Ollama-compatible chat model support using OpenAI-compatible endpoint wiring
- Provider-aware prompt execution settings (Azure vs OpenAI/Ollama)
- Optional RAG fallback in offline mode when no embedding model is configured
- Provider-aware ingestion script for vector knowledge loading
- CI/CD workflow dispatch toggle to choose offline or online model mode
- Updated `.env.example` with all required provider variables

## Model Compatibility

### Supported Provider Values

- `MODEL_PROVIDER=azure`
- `MODEL_PROVIDER=ollama`

If `MODEL_PROVIDER` is not set, startup auto-selects:

1. `azure` if Azure credentials are present
2. otherwise `ollama`

### Ollama Models

Recommended lightweight local setup:

```bash
ollama pull qwen3:8b
ollama pull nomic-embed-text
```

Then configure:

```bash
MODEL_PROVIDER=ollama
OLLAMA_BASE_URL=http://localhost:11434/v1
OLLAMA_CHAT_MODEL=qwen3:8b
OLLAMA_EMBED_MODEL=nomic-embed-text
OLLAMA_API_KEY=ollama
```

Notes:

- `OLLAMA_BASE_URL` uses OpenAI-compatible `/v1` endpoint.
- If `OLLAMA_EMBED_MODEL` is not set, chat still works and RAG calls return a safe disabled message.

## Environment Setup

Copy and edit environment file:

```bash
cp .env.example .env
```

### Offline (Ollama) Minimum Variables

```bash
MODEL_PROVIDER=ollama
OLLAMA_BASE_URL=http://localhost:11434/v1
OLLAMA_CHAT_MODEL=qwen3:8b
OLLAMA_API_KEY=ollama
```

Optional but recommended for RAG:

```bash
OLLAMA_EMBED_MODEL=nomic-embed-text
SUPABASE_DB_CONNECTION=postgresql://...
```

### Online (Azure/Foundry) Minimum Variables

```bash
MODEL_PROVIDER=azure
AZURE_OPENAI_ENDPOINT=https://<resource>.openai.azure.com/
AZURE_OPENAI_API_KEY=<key>
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini
```

Optional for RAG:

```bash
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small
SUPABASE_DB_CONNECTION=postgresql://...
```

## Run Locally

### 1) Infrastructure (optional but recommended)

```bash
docker compose -f docker/docker-compose.yml up -d
```

### 2) Python dependencies

```bash
python -m pip install -r src/langgraph/requirements.txt
```

### 3) Build and run .NET host

```bash
dotnet build src/Azentix.sln
dotnet run --project src/Azentix.AgentHost
```

Health endpoint:

- `http://localhost:5000/health`

Root endpoint shows active provider and model:

- `http://localhost:5000/`

## Verify Mode Selection

Check the root endpoint response fields:

- `modelProvider`
- `model`

Expected values:

- Azure mode: `modelProvider: "azure"`
- Ollama mode: `modelProvider: "ollama"`

## Knowledge Ingestion (Provider-Aware)

`scripts/ingest_knowledge.py` now follows `MODEL_PROVIDER`.

### Azure ingestion

```bash
MODEL_PROVIDER=azure python scripts/ingest_knowledge.py
```

### Ollama ingestion

```bash
MODEL_PROVIDER=ollama OLLAMA_EMBED_MODEL=nomic-embed-text python scripts/ingest_knowledge.py
```

## GitHub Actions Toggle (Offline vs Online)

You asked for a checkbox-like selector. GitHub Secrets themselves do not support checkboxes, so this repo now uses a workflow dispatch boolean input that behaves like a checkbox.

### CI workflow

Workflow: `.github/workflows/ci.yml`

- Input: `use_offline_model` (boolean)
- `true` maps to `MODEL_PROVIDER=ollama`
- `false` maps to `MODEL_PROVIDER=azure`

### Deploy workflow

Workflow: `.github/workflows/deploy.yml`

- Input: `use_offline_model` (boolean)
- Provider value is resolved and logged for deployment context

### Secrets Guidance

Use secrets for credentials only, not the mode toggle.

Recommended secrets when using Azure mode in GitHub:

- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_API_KEY`
- `AZURE_OPENAI_DEPLOYMENT_NAME`
- `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` (optional)

Recommended secrets when using Ollama mode in self-hosted runners:

- `OLLAMA_BASE_URL` (reachable from runner)
- `OLLAMA_CHAT_MODEL`
- `OLLAMA_EMBED_MODEL` (optional)

## Developer Test Commands

Run all project checks:

```bash
dotnet build src/Azentix.sln
dotnet test src/Azentix.sln
pytest tests/python
python scripts/test_flows.py
python scripts/test_connections.py
```

## Provider-Specific Behavior Details

- Director agent uses provider-specific SK prompt settings:
  - Azure mode: `AzureOpenAIPromptExecutionSettings`
  - Ollama mode: `OpenAIPromptExecutionSettings`
- Same plugin/tooling pipeline works in both modes.
- RAG behavior:
  - Embedding model configured: full vector search
  - Embedding model missing: safe no-op RAG response instead of runtime failure

## Troubleshooting

### `MODEL_PROVIDER=ollama` but startup fails

Check:

- Ollama running locally
- `OLLAMA_BASE_URL` includes `/v1`
- `OLLAMA_CHAT_MODEL` exists (`ollama list`)

### Azure mode selected but app exits at startup

Check:

- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_API_KEY`
- Deployment name exists in Foundry/Azure OpenAI resource

### Agent runs but RAG says disabled

Set one embedding model variable for current provider:

- Azure: `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`
- Ollama: `OLLAMA_EMBED_MODEL`

## Files Updated for Dual-Mode Support

- `src/Azentix.AgentHost/Program.cs`
- `src/Azentix.Agents/Director/DirectorAgent.cs`
- `src/Azentix.Agents/Rag/RagAgent.cs`
- `src/Azentix.Models/AgentConfiguration.cs`
- `scripts/ingest_knowledge.py`
- `.env.example`
- `.github/workflows/ci.yml`
- `.github/workflows/deploy.yml`

## License

MIT
