# 🚀 Azentix Enterprise — Multi-Agent AI Orchestration Platform

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Python 3.10+](https://img.shields.io/badge/Python-3.10+-blue.svg)](https://www.python.org/downloads/)
[![.NET 8.0+](https://img.shields.io/badge/.NET-8.0+-purple.svg)](https://dotnet.microsoft.com/download)
[![Docker](https://img.shields.io/badge/Docker-Supported-blue.svg)](https://www.docker.com/)

**Azentix Enterprise** is a production-ready, multi-agent AI orchestration platform that automates enterprise workflows across **SAP S/4HANA, Salesforce, ServiceNow, HubSpot, and Stripe**. It uses **ReAct-pattern agents** powered by **Semantic Kernel** to intelligently route, process, and synchronize complex business logic.

---

## 📋 Table of Contents

- [Quick Start](#quick-start)
- [Project Overview](#project-overview)
- [Architecture](#architecture)
  - [System Architecture Diagram](#system-architecture-diagram)
  - [Data Flow Overview](#data-flow-overview)
  - [Agent Orchestration Pattern](#agent-orchestration-pattern)
- [Core Components](#core-components)
  - [Director Agent](#director-agent)
  - [Plugins](#plugins)
  - [Memory & RAG](#memory--rag)
- [Features](#features)
- [Model Providers](#model-providers)
  - [Azure OpenAI (Online)](#azure-openai-online)
  - [Ollama (Offline)](#ollama-offline)
- [Setup & Installation](#setup--installation)
  - [Prerequisites](#prerequisites)
  - [Environment Configuration](#environment-configuration)
  - [Local Development](#local-development)
  - [Docker Deployment](#docker-deployment)
- [Configuration Guide](#configuration-guide)
- [End-to-End Workflows](#end-to-end-workflows)
  - [1. SAP-Salesforce Price Sync](#1-sap-salesforce-price-sync)
  - [2. Incident Triage & Auto-Resolution](#2-incident-triage--auto-resolution)
  - [3. Stripe Billing Alert & ServiceNow Ticketing](#3-stripe-billing-alert--servicenow-ticketing)
- [API Reference](#api-reference)
- [Development](#development)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [License](#license)

---

## 🎯 Quick Start

### **Option 1: Run Locally with Ollama (Completely Free & Offline)**

```bash
# Clone the repository
git clone https://github.com/your-org/azentix-enterprise.git
cd azentix-enterprise

# 1. Install Ollama from https://ollama.ai
# 2. Pull lightweight models
ollama pull qwen3:8b
ollama pull nomic-embed-text

# 3. Start Ollama in a separate terminal
ollama serve

# 4. Configure environment
cp .env.example .env
# Edit .env: Set MODEL_PROVIDER=ollama

# 5. Install .NET dependencies and run
cd src
dotnet restore
dotnet build
dotnet run --project Azentix.AgentHost/Azentix.AgentHost.csproj

# Agent Host is now running on http://localhost:5000
```

### **Option 2: Deploy with Docker Compose (All Services Included)**

```bash
# Start the full stack (RabbitMQ, n8n, Kong, Agent Host, Supabase mock)
docker-compose -f docker/docker-compose.yml up -d

# Wait for services to be ready (~30 seconds)
# Access points:
# - Agent Host:    http://localhost:5000
# - Kong Gateway:  http://localhost:8000
# - n8n Workflows: http://localhost:5678
# - RabbitMQ:      http://localhost:15672 (guest/guest)
```

### **Option 3: Deploy to Azure (Production)**

See [Deployment](#deployment) section for step-by-step instructions.

---

## 📊 Project Overview

### **What Azentix Does**

Azentix Enterprise solves the challenge of **orchestrating complex business logic across siloed enterprise systems**. Instead of building brittle point-to-point integrations, Azentix uses **AI agents** to:

✅ **Read** data from multiple systems intelligently  
✅ **Reason** about business rules and policies  
✅ **Execute** actions with validation and approval workflows  
✅ **Remember** decisions using RAG-powered knowledge retrieval  
✅ **Adapt** to new workflows without code changes  

### **Problem Statement**

Enterprises struggle with:
- **Data silos**: SAP has prices, Salesforce has opportunities, ServiceNow has policies
- **Manual workflows**: Employees manually copy data between systems
- **Brittle integrations**: Hard-coded ETL jobs fail silently
- **No intelligence**: Integrations can't apply business logic or policies

### **Azentix Solution**

**AI-powered orchestration** that treats each system interaction as a **reasoned decision**, not a mechanical copy.

---

## 🏗️ Architecture

### **System Architecture Diagram**

```
┌─────────────────────────────────────────────────────────────────────┐
│                   ENTERPRISE SYSTEMS LAYER                          │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐│
│  │SAP       │  │Salesforce│  │ServiceNow│  │HubSpot   │  │Stripe  ││
│  │S/4HANA   │  │CRM       │  │ITSM     │  │Marketing │  │Billing ││
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘  └───┬────┘│
└───────┼─────────────┼─────────────┼─────────────┼────────────┼──────┘
        │             │             │             │            │
        │ Events / Webhooks (JSON payloads)       │            │
        │                                          │            │
┌───────▼──────────────────────────────────────────▼────────────▼──────┐
│                   MESSAGING LAYER (CloudAMQP)                         │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │  RabbitMQ Queues & Topics                                       │ │
│  │  • sap-price-changes    • servicenow-incidents                  │ │
│  │  • hubspot-sync         • stripe-billing-alerts                 │ │
│  │  • approval-queue       • notifications                         │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└───────┬──────────────────────────────────────────────────────────────┘
        │
        │ Queue Triggers
        │
┌───────▼──────────────────────────────────────────────────────────────┐
│                   WORKFLOW LAYER (n8n)                                │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────────────┐ │
│  │ SAP Price Sync   │  │ Incident Triage  │  │ Stripe Billing     │ │
│  │ Workflow         │  │ Workflow         │  │ Alert Workflow     │ │
│  └────────┬─────────┘  └────────┬─────────┘  └────────┬───────────┘ │
│           │                     │                     │              │
│           └─────────┬───────────┴─────────────────────┘              │
│                     │                                                │
│                     │ HTTP POST with X-API-Key Auth                  │
│                     │                                                │
└─────────────────────┼────────────────────────────────────────────────┘
                      │
┌─────────────────────▼────────────────────────────────────────────────┐
│                    API GATEWAY (Kong)                                │
│  ┌─────────────────────────────────────────────────────────────────┐│
│  │ Rate Limiting │ Authentication │ Caching │ Load Balancing │Logs ││
│  │ Max: 60 req/min | Cache: 5min | Metrics & Monitoring           ││
│  └─────────────────────────────────────────────────────────────────┘│
│                             │                                        │
│                    Proxy → Agent Host                                │
└─────────────────────┬────────────────────────────────────────────────┘
                      │
┌─────────────────────▼────────────────────────────────────────────────┐
│                 AGENT ORCHESTRATION (Azentix Host)                   │
│  ┌──────────────────────────────────────────────────────────────────┐│
│  │                    DirectorAgent (ReAct Pattern)                  ││
│  │  • Semantic Kernel powered reasoning engine                       ││
│  │  • gpt-4o-mini or qwen3:8b (configurable)                        ││
│  │  • Multi-step planning & tool execution                          ││
│  │                                                                   ││
│  │  Plugins:                                                        ││
│  │  ┌────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐  ┌────────┐││
│  │  │SAP     │  │Salesforce│  │ServiceNow│  │HubSpot │  │Stripe  │││
│  │  │Plugin  │  │Plugin    │  │Plugin    │  │Plugin  │  │Plugin  │││
│  │  └────────┘  └──────────┘  └──────────┘  └────────┘  └────────┘││
│  │  ┌──────────────┐  ┌──────────────────┐  ┌────────────────────┐││
│  │  │MemoryAgent   │  │RabbitMQPlugin    │  │RAGPlugin           │││
│  │  │(Vector Mem)  │  │(Queue Manager)   │  │(Knowledge Retrieval)││
│  │  └──────────────┘  └──────────────────┘  └────────────────────┘││
│  └──────────────────────────────────────────────────────────────────┘│
│                                                                       │
│  ASP.NET Core WebAPI (.NET 8):                                       │
│  • /api/agents/execute          (POST)  — Execute task               │
│  • /api/agents/status           (GET)   — Health check               │
│  • /health                      (GET)   — ASP.NET Health Probe       │
└───────────────────────┬──────────────────────────────────────────────┘
                        │
                        │ pgvector Queries
                        │ (Semantic Search)
                        │
┌───────────────────────▼──────────────────────────────────────────────┐
│                  KNOWLEDGE LAYER (Supabase pgvector)                 │
│  ┌──────────────────────────────────────────────────────────────────┐│
│  │  PostgreSQL + pgvector (HNSW Index)                               ││
│  │  ┌─────────────────────────────────────────────────────────────┐││
│  │  │ Collections:                                                │││
│  │  │ • sap-salesforce-sync     (pricing rules, sync policies)   │││
│  │  │ • servicenow-kb           (incident resolution knowledge) │││
│  │  │ • stripe-policies         (billing rules & regulations)   │││
│  │  │ • default                 (general enterprise knowledge)  │││
│  │  └─────────────────────────────────────────────────────────────┘││
│  │  Functions:                                                      ││
│  │  • match_documents()  — Semantic search with similarity scoring  ││
│  │  • insert_memory()    — Store agent memory & decisions           ││
│  │  • query_collections()— Retrieve context for RAG                 ││
│  └──────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────┘

        PERSISTENCE & MONITORING LAYER (Grafana + Logs)
  ┌─────────────────────────────────────────────────────┐
  │ Grafana Dashboards:                                 │
  │ • Agent Performance (latency, success rate)         │
  │ • Plugin Health (API calls, errors)                 │
  │ • Workflow Metrics (throughput, queue depth)        │
  │ • Knowledge Base Usage (similarity scores, hits)    │
  └─────────────────────────────────────────────────────┘
```

### **Data Flow Overview**

```
STEP 1: Event Ingestion
└─ External System (SAP/Salesforce/etc.) triggers webhook
   └─ Payload is validated and published to RabbitMQ

STEP 2: Workflow Orchestration (n8n)
└─ n8n consumes message from queue
   └─ Extracts relevant fields & context
   └─ Calls Agent Host API endpoint

STEP 3: Agent Reasoning (Azentix Director)
└─ DirectorAgent receives AgentTask
   └─ Validates pre-conditions (credentials, business rules)
   └─ Executes ReAct loop:
      ├─ Thought: Plan the action sequence
      ├─ Action: Call relevant plugin
      ├─ Observation: Process plugin response
      └─ Repeat until Final Answer achieved
   └─ RAG queries knowledge base for policies/rules
   └─ Returns structured AgentResult

STEP 4: Plugin Execution
└─ Each plugin (SAP, Salesforce, etc.)
   └─ Authenticates to target system
   └─ Executes read or write operation
   └─ Returns data or confirmation

STEP 5: Memory & Knowledge Update
└─ Agent memory stored in Supabase pgvector
   └─ Embeddings created for semantic search
   └─ Future similar tasks can reuse context

STEP 6: Webhook Response (n8n)
└─ n8n receives AgentResult
   └─ Publishes success/failure to additional queues
   └─ Triggers downstream automations
```

### **Agent Orchestration Pattern (ReAct)**

```
┌─────────────────────────────────────────────────────────────┐
│            Agent Task Received                              │
│            (TaskId, TaskType, Payload)                      │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  1️⃣  PRE-VALIDATION LAYER                                   │
│     ├─ Check credentials configured?                       │
│     ├─ Check task prerequisites met?                       │
│     └─ Return early if validation fails                    │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  2️⃣  RAG CONTEXT RETRIEVAL (Optional)                       │
│     └─ Query knowledge base for similar past decisions      │
│        └─ Score & rank by similarity                        │
│        └─ Add context to system prompt                      │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  3️⃣  ReAct LOOP (Multi-Turn Agent)                          │
│                                                             │
│  Iteration N:                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Thought: [LLM generates reasoning & plan]            │  │
│  │                                                       │  │
│  │ Action: plugin_name(param1=val1, param2=val2)       │  │
│  │ Observation: [Plugin response or error]              │  │
│  │                                                       │  │
│  │ → Parse Observation                                  │  │
│  │    ├─ If task complete → proceed to Step 4           │  │
│  │    ├─ If error & retryable → loop back to Thought    │  │
│  │    ├─ If confidence < 0.7 → escalate to human        │  │
│  │    └─ If max iterations reached → timeout            │  │
│  │                                                       │  │
│  └──────────────────────────────────────────────────────┘  │
│                         ↑                                   │
│                         │                                   │
│        Continue until Final Answer or error                │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  4️⃣  MEMORY PERSISTENCE                                     │
│     └─ Store decision & context in pgvector                │
│        └─ Create embedding for future retrieval            │
│        └─ Tag with task type & collection                  │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  5️⃣  RESULT RETURN                                          │
│     ├─ Status: Success / PartialSuccess / Failed            │
│     ├─ AgentResult: {TaskId, Status, Output, Metadata}    │
│     └─ Logs: Execution trace for audit & debugging         │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔧 Core Components

### **1. Director Agent**

The **intelligence core** of Azentix. Built with **Microsoft Semantic Kernel**, it orchestrates multi-step workflows using the **ReAct (Reasoning + Acting)** pattern.

**Key Characteristics:**
- **LLM-agnostic**: Works with Azure OpenAI, Ollama, or any OpenAI-compatible API
- **Tool-calling**: Automatic plugin selection based on task type
- **Stateful**: Can maintain conversation history and multi-step reasoning
- **Auditable**: Every thought and action is logged
- **Fail-safe**: Validates all outputs before execution

**System Prompt (Simplified):**
```
"You are Azentix Director — an enterprise AI agent.
Follow the ReAct pattern:
  Thought → Action → Observation → Final Answer
Rules:
- Always reason before acting
- Validate data before writing
- If confidence < 0.7, require human review
- Don't expose credentials"
```

### **2. Plugins**

**Plugin** = API wrapper around a business system. Each plugin handles:
- Authentication (OAuth, API keys, Kerberos)
- API calls with retry logic
- Response parsing & error handling
- Business logic validation

| Plugin | System | Operations |
|--------|--------|-----------|
| **SapPlugin** | SAP S/4HANA | Read products, prices; Update pricing tables |
| **SalesforcePlugin** | Salesforce CRM | Read/update opportunities, accounts, quotes |
| **ServiceNowPlugin** | ServiceNow ITSM | Create/resolve incidents, update KBs |
| **HubSpotPlugin** | HubSpot CRM | Sync contacts, deals, company info |
| **StripePlugin** | Stripe Billing | Fetch invoices, customer data, subscriptions |
| **RabbitMQPlugin** | CloudAMQP | Publish/consume messages, manage queues |
| **RagPlugin** | Supabase pgvector | Search knowledge base, store decisions |

**Example: SalesforcePlugin.cs**
```csharp
public class SalesforcePlugin
{
    [KernelFunction("get_opportunity")]
    public async Task<string> GetOpportunity(string opportunityId)
    {
        var client = new HttpClient() { DefaultRequestHeaders = _headers };
        var resp = await client.GetAsync($"{_endpoint}/opportunities/{opportunityId}");
        return await resp.Content.ReadAsStringAsync();
    }
}
```

### **3. Memory & RAG**

**Retrieval-Augmented Generation (RAG)** enables agents to:
- Learn from past decisions without fine-tuning
- Apply company policies consistently
- Reduce hallucinations by grounding decisions in knowledge

**Architecture:**
- **Vector Store**: Supabase pgvector (PostgreSQL + HNSW index)
- **Embeddings**: Azure Text-Embedding-3-small or nomic-embed-text (Ollama)
- **Collections**: Organized by domain (SAP, ServiceNow, Stripe, default)
- **Retrieval**: Match documents() function with similarity scoring

**How It Works:**
```
1. Agent encounters a new task
2. Embeds task description
3. Queries Supabase pgvector: CALL match_documents('sap-salesforce-sync', embedding, 5)
4. Returns top-5 similar past decisions with scores
5. Agent incorporates retrieved context into prompt
6. After execution, stores new decision for future reference
```

---

## ✨ Features

| Feature | Description | Benefit |
|---------|-----------|---------|
| **🤖 AI-Powered Reasoning** | ReAct pattern with multi-turn thought chains | Intelligent, adaptable automation |
| **🔌 Multi-System Integration** | SAP, Salesforce, ServiceNow, HubSpot, Stripe | Enterprise-wide orchestration |
| **🧠 Vector Memory & RAG** | pgvector-based semantic search | Context-aware decisions |
| **🔄 Dual LLM Support** | Azure OpenAI (online) + Ollama (offline) | Flexibility, cost control |
| **☁️ Cloud-Native** | Docker, Kubernetes-ready, ASP.NET Core | Scalable, DevOps-friendly |
| **🔐 Enterprise Security** | RBAC, audit logs, credential management | Compliance-ready |
| **⚡ Real-Time Webhooks** | Event-driven via RabbitMQ | Low-latency automation |
| **📊 Observability** | Grafana dashboards, structured logging | Production visibility |
| **🚫 No Vendor Lock-In** | Works with self-hosted or cloud services | Freedom & portability |

---

## 🧠 Model Providers

### **Azure OpenAI (Online — Production)**

**When to Use:** Production workloads, need latest models (GPT-4o), require Azure compliance.

**Setup:**

```bash
# 1. Create Azure OpenAI resource in Azure Portal
# 2. Deploy a model (e.g., gpt-4o-mini for cost efficiency)
# 3. Grab your endpoint and key

# 3. Set environment variables
export MODEL_PROVIDER=azure
export AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
export AZURE_OPENAI_API_KEY=your_key_here
export AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini
export AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small  # optional

# 4. Run
dotnet run
```

**Cost:** ~$0.06 per 1K prompt tokens (gpt-4o-mini) + $200 free trial.

### **Ollama (Offline — Development & Privacy)**

**When to Use:** Development, offline environments, data privacy, zero cloud costs.

**Setup:**

```bash
# 1. Install Ollama from https://ollama.ai

# 2. Pull models
ollama pull qwen3:8b        # Chat model (~4.5 GB)
ollama pull nomic-embed-text # Embeddings (~280 MB)

# 3. Start Ollama server (runs on port 11434)
ollama serve

# 4. In another terminal, set environment
export MODEL_PROVIDER=ollama
export OLLAMA_BASE_URL=http://localhost:11434/v1
export OLLAMA_CHAT_MODEL=qwen3:8b
export OLLAMA_EMBED_MODEL=nomic-embed-text
export OLLAMA_API_KEY=ollama

# 5. Run Azentix
dotnet run
```

**Cost:** $0. Runs 100% locally, no cloud dependencies.

**Performance:** Qwen3:8B is fastest for reasoning; requires 8GB+ RAM.

### **Comparison Table**

| Aspect | Azure OpenAI | Ollama |
|--------|--------------|--------|
| **Cost** | ~$0.06/1K tokens | $0 |
| **Latency** | 500-1000ms | 2-5s (on M1 Mac) |
| **Internet** | Required | Optional |
| **Models** | GPT-4o, GPT-4, etc. | Qwen, Llama, Mistral |
| **Setup Time** | 10 min (portal) | 5 min (ollama pull) |
| **Best For** | Production, performance | Dev, privacy, cost |

---

## 🚀 Setup & Installation

### **Prerequisites**

#### **Global Requirements**
- **Git**: For cloning & version control
- **Docker & Docker Compose**: For containerized deployment
- **Python 3.10+**: For LangGraph workflows & scripts

#### **For .NET Development**
- **.NET 8 SDK**: [Download](https://dotnet.microsoft.com/download)
- **Visual Studio Code** or **Visual Studio 2022+**

#### **For Model Execution**
- **Option A: Azure Subscription**
  - Azure OpenAI resource
  - Deployment of gpt-4o-mini
  - API key & endpoint
  
- **Option B: Ollama**
  - Ollama installed locally
  - `qwen3:8b` & `nomic-embed-text` models pulled

#### **For Data Storage**
- **Supabase Account** (free tier) for pgvector
  - Database connection string
  - Service role key

#### **For Messaging**
- **CloudAMQP Account** (free tier) or local RabbitMQ
  - Connection URL

### **Environment Configuration**

#### **Step 1: Clone Repository**

```bash
git clone https://github.com/your-org/azentix-enterprise.git
cd azentix-enterprise
```

#### **Step 2: Copy Environment Template**

```bash
cp .env.example .env
```

#### **Step 3: Fill Configuration**

Edit `.env` with your values:

```bash
# Choose your model provider
MODEL_PROVIDER=azure  # or 'ollama'

# ===== AZURE OPENAI (if MODEL_PROVIDER=azure) =====
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_API_KEY=your_key_here
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small

# ===== OLLAMA (if MODEL_PROVIDER=ollama) =====
OLLAMA_BASE_URL=http://localhost:11434/v1
OLLAMA_CHAT_MODEL=qwen3:8b
OLLAMA_EMBED_MODEL=nomic-embed-text
OLLAMA_API_KEY=ollama

# ===== SUPABASE (Free tier: 500MB, 50,000 max rows) =====
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=eyJhbGci...
SUPABASE_SERVICE_KEY=eyJhbGci...
SUPABASE_DB_CONNECTION=postgresql://postgres:PASSWORD@db.xxx.supabase.co:5432/postgres

# ===== RABBIT MQ (CloudAMQP free: 1M msgs/month) =====
CLOUDAMQP_URL=amqps://user:password@host/vhost

# ===== SYSTEM INTEGRATIONS =====
# SAP S/4HANA
SAP_URL=https://sap-instance.example.com:50000
SAP_CLIENT=100
SAP_USERNAME=APIUSER
SAP_PASSWORD=secure_password

# Salesforce
SALESFORCE_INSTANCE=https://your-instance.salesforce.com
SALESFORCE_CLIENT_ID=3MVG9l...
SALESFORCE_CLIENT_SECRET=your_secret...
SALESFORCE_USERNAME=api@company.com
SALESFORCE_PASSWORD=your_password

# ServiceNow
SERVICENOW_INSTANCE=https://dev12345.service-now.com
SERVICENOW_USERNAME=admin
SERVICENOW_PASSWORD=your_password

# HubSpot
HUBSPOT_API_KEY=pat-na1-...

# Stripe
STRIPE_API_KEY=sk_live_...
STRIPE_WEBHOOK_SECRET=whsec_...
```

⚠️ **Never commit `.env` to git!** It's in `.gitignore` by default.

### **Local Development**

#### **Option 1: Run Directly with .NET CLI**

```bash
# 1. Install .NET 8 (if needed)
# https://dotnet.microsoft.com/download

# 2. Navigate to source
cd src

# 3. Restore NuGet packages
dotnet restore

# 4. Build the solution
dotnet build

# 5. Run Agent Host
dotnet run --project Azentix.AgentHost/Azentix.AgentHost.csproj

# Output:
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: http://localhost:5000
#       Application started.
```

**Verify It's Working:**

```bash
curl http://localhost:5000/api/agents/status
# Expected: {"status":"ready","timestamp":"2025-04-20T10:30:00Z","version":"1.0.0"}
```

#### **Option 2: Run via Docker Compose (Recommended)**

```bash
# Start all services at once
docker-compose -f docker/docker-compose.yml up -d

# Wait for services to stabilize (~30s)
sleep 30

# Check logs
docker-compose -f docker/docker-compose.yml logs -f azentix-host

# Verify endpoints
curl http://localhost:5000/api/agents/status        # Agent Host
curl http://localhost:8000/api/agents/status        # Kong Gateway
curl http://localhost:5678/                         # n8n UI
```

### **Docker Deployment**

#### **Build Docker Image**

```bash
# Build the Azentix Agent Host image
docker build -f src/Azentix.AgentHost/Dockerfile \
  -t azentix-host:latest \
  -t azentix-host:$(git rev-parse --short HEAD) \
  .

# Tag for registry (example: Azure Container Registry)
docker tag azentix-host:latest \
  your-registry.azurecr.io/azentix-host:latest

# Push to registry
docker push your-registry.azurecr.io/azentix-host:latest
```

#### **Run Standalone Container**

```bash
docker run -d \
  --name azentix-host \
  -p 5000:5000 \
  -e MODEL_PROVIDER=ollama \
  -e OLLAMA_BASE_URL=http://host.docker.internal:11434/v1 \
  -e SUPABASE_URL=https://your-project.supabase.co \
  -e SUPABASE_SERVICE_KEY=your_key \
  azentix-host:latest
```

---

## ⚙️ Configuration Guide

### **DirectorAgent Configuration**

Edit `appsettings.json` to control agent behavior:

```json
{
  "AgentConfig": {
    "MaxIterations": 10,           // Max ReAct loops before timeout
    "ContextWindowSize": 2000,     // Token limit for context
    "RagEnabled": true,            // Enable RAG queries
    "RagTopK": 5,                  // Number of similar docs to retrieve
    "ConfidenceThreshold": 0.7,    // Min confidence to auto-approve
    "TimeoutSeconds": 30           // Max execution time per task
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### **Plugin Configuration**

Each plugin reads from environment variables:

```csharp
// Example: SapPlugin auto-configures from env
var sapUrl = cfg["SAP_URL"];           // https://sap-instance:50000
var sapClient = cfg["SAP_CLIENT"];     // 100
var sapUsername = cfg["SAP_USERNAME"]; // APIUSER
```

### **RAG Collections**

Pre-create collections in Supabase for different domains:

```sql
-- Create vector table (if not exists)
CREATE TABLE agent_memory (
  id BIGSERIAL PRIMARY KEY,
  collection_name TEXT NOT NULL,
  task_type TEXT,
  embedding vector(384),  -- For nomic-embed-text
  content TEXT,
  metadata JSONB,
  created_at TIMESTAMP DEFAULT NOW(),
  CONSTRAINT fk_collection FOREIGN KEY (collection_name) REFERENCES rag_collections(name)
);

CREATE INDEX ON agent_memory USING HNSW (embedding vector_cosine_ops);

-- Insert collections
INSERT INTO rag_collections (name, description) VALUES
  ('sap-salesforce-sync', 'Pricing rules & sync policies'),
  ('servicenow-kb', 'Incident resolution knowledge'),
  ('stripe-policies', 'Billing rules & regulations'),
  ('default', 'General enterprise knowledge');
```

---

## 🔄 End-to-End Workflows

### **1. SAP-Salesforce Price Sync**

**Scenario:** Daily price changes in SAP must auto-sync to Salesforce opportunities if they meet approval criteria.

**Flow Diagram:**

```
┌─────────────────┐
│ SAP S/4HANA     │
│ Price Updated   │
│ (Webhook)       │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────┐
│ RabbitMQ Queue              │
│ "sap-price-changes"         │
└────────┬────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ n8n Workflow                │
│ "SAP Price Sync"            │
│ 1. Parse SAP event          │
│ 2. Extract product ID, price│
│ 3. Call Agent Host API      │
└────────┬────────────────────┘
         │
         ▼
    Kong Gateway (Auth + Rate Limit)
         │
         ▼
┌───────────────────────────────────────────┐
│ DirectorAgent: task-type = "sap-           │
│                            salesforce-    │
│                            price-sync"    │
│                                           │
│ 1. Thought: Check if price change > 5%   │
│ 2. Action: SapPlugin.GetProduct()        │
│    Observation: {product_id, price}      │
│ 3. Action: SalesforcePlugin.             │
│            SearchOpportunities()         │
│    Observation: [opportunity 1, 2, 3]   │
│ 4. Action: RagPlugin.QueryKB()           │
│    Observation: "Auto-sync if value      │
│                  > $50K" (policy)        │
│ 5. Thought: All opps meet criteria       │
│ 6. Action: SalesforcePlugin.             │
│            UpdateQuotes() ✓              │
│ 7. Thought: Store decision in memory     │
│ 8. Action: RagPlugin.StoreMem()          │
│ Final Answer: Success, 3 quotes updated  │
└───────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ n8n: Parse Agent Result             │
│ • status = Success                  │
│ • updated_quotes = [Q1, Q2, Q3]     │
│ • confidence = 0.95                 │
└────────┬────────────────────────────┘
         │
         ▼
┌──────────────────────────────────┐
│ Publish to RabbitMQ              │
│ Queue: "sync-complete"           │
│ Send Slack notification:         │
│ "Synced 3 SF quotes from SAP"    │
└──────────────────────────────────┘
```

**Agent Execution:**

```bash
# Call the Agent Host
curl -X POST http://localhost:5000/api/agents/execute \
  -H "Content-Type: application/json" \
  -d '{
    "taskId": "task-20250420-sap-001",
    "taskType": "sap-salesforce-price-sync",
    "priority": "high",
    "payload": {
      "product_id": "PROD-12345",
      "old_price": 100,
      "new_price": 106,
      "sap_doc_id": "ORDER-2025-004"
    }
  }'

# Response:
{
  "taskId": "task-20250420-sap-001",
  "status": "Success",
  "output": {
    "synced_opportunities": 3,
    "updated_quotes": [
      {"quote_id": "Q1001", "old_price": 100, "new_price": 106},
      {"quote_id": "Q1002", "old_price": 100, "new_price": 106},
      {"quote_id": "Q1003", "old_price": 100, "new_price": 106}
    ],
    "confidence": 0.95,
    "reasoning": "All opportunities are high-value (>$50K), 
                  product matches SAP data, and auto-sync policy applies"
  },
  "executionTimeMs": 2450,
  "errors": []
}
```

### **2. Incident Triage & Auto-Resolution**

**Scenario:** ServiceNow incidents come in. Agent evaluates if they match known patterns and can auto-resolve; otherwise, escalate.

**Flow Diagram:**

```
┌─────────────────────┐
│ ServiceNow          │
│ New Incident        │
│ (REST API)          │
└────────┬────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ n8n Workflow: "Incident Triage" │
└────────┬────────────────────────┘
         │
         ▼
    Kong Gateway
         │
         ▼
┌─────────────────────────────────────────────────┐
│ DirectorAgent: task-type =                      │
│   "servicenow-incident-triage"                  │
│                                                 │
│ 1. Thought: Analyze incident description       │
│ 2. Action: RagPlugin.QueryKB()                 │
│    Observation: Retrieved 3 KB articles        │
│                 (similarity: 0.92, 0.87, 0.81)│
│ 3. Thought: Strong match (0.92) - check       │
│             if incident is resolvable          │
│ 4. Action: ServiceNowPlugin.GetIncident()      │
│    Observation: {                              │
│      "title": "Password reset request",        │
│      "caller": "user@company.com",             │
│      "state": "new"                            │
│    }                                           │
│ 5. Thought: Matches KB article #287 -          │
│             auto-resolvable with 0.92 score    │
│ 6. Action: ServiceNowPlugin.ResolveIncident()  │
│    Observation: Incident INC0054321 resolved   │
│ 7. Action: RagPlugin.StoreMem()                │
│    Observation: Memory stored                  │
│ Final Answer: Auto-resolved, confidence 0.92   │
│              KB article #287 applied           │
└─────────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────┐
│ n8n Response Handler                     │
│ • status = Success                       │
│ • action_taken = "resolved"              │
│ • resolution_article = "KB-287"          │
│ • confidence = 0.92                      │
│ • escalation_reason = null               │
└────────┬─────────────────────────────────┘
         │
         ▼
    Send Confirmation Email to Caller
    (Ticket resolved automatically)

┌──────────────────────────────────────────┐
│ ALTERNATIVE: Low Confidence / Escalation │
│                                          │
│ If similarity < 0.7 or incident complex: │
│ 1. Set status = HumanReviewRequired      │
│ 2. Assign to ServiceNow support group    │
│ 3. Tag with AI triage notes              │
│ 4. Send Slack alert to team              │
└──────────────────────────────────────────┘
```

**Agent Execution:**

```bash
curl -X POST http://localhost:5000/api/agents/execute \
  -H "Content-Type: application/json" \
  -d '{
    "taskId": "task-20250420-sn-incident-098",
    "taskType": "servicenow-incident-triage",
    "priority": "high",
    "payload": {
      "incident_id": "INC0054321",
      "title": "Cannot reset password",
      "description": "User locked out after 3 failed attempts",
      "caller_id": "user@company.com",
      "priority": "2"
    }
  }'

# Response:
{
  "taskId": "task-20250420-sn-incident-098",
  "status": "Success",
  "output": {
    "action": "auto_resolved",
    "resolution_notes": "Matched KB article KB-287: Password Reset Policy. 
                         User account unlocked. Password reset link sent.",
    "kb_article_id": "KB-287",
    "similarity_score": 0.92,
    "confidence": 0.92,
    "resolution_time_minutes": 0,
    "escalation_required": false
  },
  "executionTimeMs": 1850,
  "errors": []
}
```

### **3. Stripe Billing Alert & ServiceNow Ticketing**

**Scenario:** Stripe detects failed payment. Agent creates ServiceNow ticket, flags Salesforce account, updates HubSpot contact.

**Flow Diagram:**

```
┌──────────────────────┐
│ Stripe Webhook       │
│ charge.failed        │
└────────┬─────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ RabbitMQ Queue                  │
│ "stripe-billing-alerts"         │
└────────┬────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────┐
│ n8n: "Stripe Billing Alert Workflow"         │
└────────┬─────────────────────────────────────┘
         │
         ▼
    Kong Gateway
         │
         ▼
┌────────────────────────────────────────────────────┐
│ DirectorAgent: task-type =                         │
│   "stripe-payment-failed-escalation"               │
│                                                    │
│ 1. Thought: Customer payment failed, need multi-   │
│             system escalation                      │
│ 2. Action: StripePlugin.GetCustomer()             │
│    Observation: {                                  │
│      "id": "cus_ABC123",                           │
│      "email": "company@acme.com",                  │
│      "name": "ACME Corp",                          │
│      "amount_failed": 5000.00,                     │
│      "failure_reason": "card_declined"            │
│    }                                               │
│ 3. Action: SalesforcePlugin.GetAccount()          │
│    Observation: Account SF-12456 (ACME)           │
│ 4. Action: ServiceNowPlugin.CreateIncident()      │
│    Observation: INC0054322 created (urgent)       │
│ 5. Action: HubSpotPlugin.UpdateContact()          │
│    Observation: Contact flagged "payment_failed"  │
│ 6. Action: SalesforcePlugin.UpdateAccount()       │
│    Observation: Account status: at_risk           │
│ 7. Action: RagPlugin.StoreMem()                   │
│    Observation: Escalation logged                 │
│ Final Answer: Multi-system escalation complete    │
└────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────┐
│ n8n Response Handler                               │
│ • Sends email to Customer Success team            │
│ • Posts to Slack #billing-alerts                  │
│ • Triggers Salesforce Account Review flow         │
└────────────────────────────────────────────────────┘
```

**Agent Execution:**

```bash
curl -X POST http://localhost:5000/api/agents/execute \
  -H "Content-Type: application/json" \
  -d '{
    "taskId": "task-20250420-stripe-billing-001",
    "taskType": "stripe-payment-failed-escalation",
    "priority": "urgent",
    "payload": {
      "stripe_customer_id": "cus_ABC123",
      "charge_id": "ch_1234567890",
      "amount_cents": 500000,
      "failure_reason": "card_declined",
      "attempt_count": 1
    }
  }'

# Response:
{
  "taskId": "task-20250420-stripe-billing-001",
  "status": "Success",
  "output": {
    "escalation_actions": [
      {
        "system": "ServiceNow",
        "action": "incident_created",
        "incident_id": "INC0054322",
        "priority": "1_urgent",
        "assigned_to": "billing-support-team"
      },
      {
        "system": "HubSpot",
        "action": "contact_flagged",
        "contact_id": "hs-contact-5678",
        "status": "payment_failed"
      },
      {
        "system": "Salesforce",
        "action": "account_updated",
        "account_id": "SF-12456",
        "status": "at_risk",
        "health_status": "red"
      }
    ],
    "customer_info": {
      "name": "ACME Corp",
      "email": "company@acme.com",
      "total_owed": 5000.00,
      "retry_scheduled": "2025-04-21T14:30:00Z"
    },
    "confidence": 0.98
  },
  "executionTimeMs": 3200,
  "errors": []
}
```

---

## 📡 API Reference

### **Execute Agent Task**

**Endpoint:** `POST /api/agents/execute`

**Authentication:** X-API-Key (via Kong Gateway)

**Request:**

```json
{
  "taskId": "unique-task-identifier",
  "taskType": "sap-salesforce-price-sync|servicenow-incident-triage|stripe-payment-failed-escalation|custom-type",
  "priority": "low|normal|high|urgent",
  "payload": {
    "any-business-data": "value",
    "nested": { "json": "structure" }
  }
}
```

**Response (Success):**

```json
{
  "taskId": "unique-task-identifier",
  "status": "Success|PartialSuccess|Failed|HumanReviewRequired",
  "output": {
    "action_taken": "...",
    "result": "...",
    "confidence": 0.95
  },
  "executionTimeMs": 2450,
  "errors": []
}
```

**Response (Error):**

```json
{
  "taskId": "unique-task-identifier",
  "status": "Failed",
  "output": null,
  "executionTimeMs": 500,
  "errors": [
    {
      "code": "PluginExecutionFailed",
      "message": "Salesforce API returned 401 Unauthorized"
    }
  ]
}
```

---

### **Agent Health Check**

**Endpoint:** `GET /api/agents/status`

**Response:**

```json
{
  "status": "ready",
  "timestamp": "2025-04-20T10:30:00Z",
  "version": "1.0.0"
}
```

---

## 👨‍💻 Development

### **Project Structure**

```
src/
├── Azentix.Models/                  # Shared data classes
│   ├── AgentTask.cs
│   ├── AgentResult.cs
│   ├── AgentConfiguration.cs
│   └── *Configuration.cs            # System-specific configs
├── Azentix.Agents/                  # Core agent logic
│   ├── Director/
│   │   ├── DirectorAgent.cs
│   │   ├── IDirectorAgent.cs
│   │   └── DirectorTaskRules.cs
│   ├── Action/
│   │   └── ActionAgent.cs
│   ├── Memory/
│   │   ├── MemoryAgent.cs
│   │   ├── IMemoryAgent.cs
│   │   └── SupabaseVectorMemory.cs
│   ├── Plugins/
│   │   ├── SapPlugin.cs
│   │   ├── SalesforcePlugin.cs
│   │   ├── ServiceNowPlugin.cs
│   │   ├── HubSpotPlugin.cs
│   │   ├── StripePlugin.cs
│   │   ├── RabbitMQPlugin.cs
│   │   └── RagPlugin.cs
│   └── Rag/
│       └── RagService.cs
├── Azentix.AgentHost/               # ASP.NET Core entry point
│   ├── Program.cs
│   ├── Controllers/
│   │   ├── AgentController.cs
│   │   ├── HealthController.cs
│   │   └── WebhookController.cs
│   ├── appsettings.json
│   └── Dockerfile

langgraph/                            # Python LangGraph workflows
├── flows/
│   ├── price_sync_flow.py
│   ├── incident_triage_flow.py
│   └── stripe_billing_flow.py
└── requirements.txt

n8n-workflows/                        # n8n workflow definitions
├── sap-price-sync.json
├── incident-triage.json
└── stripe-billing-alert.json

tests/                                # Unit & integration tests
├── conftest.py
└── python/
    └── test_flows.py

docker/                               # Docker Compose & configs
├── docker-compose.yml
└── Dockerfile

kong/                                 # Kong API Gateway config
└── kong.yml

grafana/                              # Monitoring dashboards
├── agent-config.yaml
└── dashboards/
    └── azentix-overview.json

docs/                                 # Architecture & guides
├── architecture/
│   └── stack-comparison.md
└── setup/
    └── supabase-schema.sql
```

### **Running Tests**

```bash
# Unit tests (.NET)
cd src
dotnet test Azentix.Tests.Unit/

# Python integration tests
cd ../tests
pytest python/test_flows.py -v

# With coverage
pytest --cov=langgraph --cov-report=html
```

### **Adding a New Plugin**

1. **Create plugin class in `Azentix.Agents/Plugins/`**

```csharp
using Microsoft.SemanticKernel;

namespace Azentix.Agents.Plugins;

public class MySystemPlugin
{
    private readonly string _apiKey;
    private readonly string _endpoint;

    public MySystemPlugin(IConfiguration cfg)
    {
        _apiKey = cfg["MY_SYSTEM_API_KEY"];
        _endpoint = cfg["MY_SYSTEM_ENDPOINT"];
    }

    [KernelFunction("get_data")]
    public async Task<string> GetData(string id)
    {
        var client = new HttpClient() { DefaultRequestHeaders = AuthHeaders };
        var resp = await client.GetAsync($"{_endpoint}/data/{id}");
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("update_data")]
    public async Task<string> UpdateData(string id, string newData)
    {
        // Implement update logic
    }
}
```

2. **Register in `Program.cs`**

```csharp
builder.Services.AddSingleton(new MySystemPlugin(cfg));
kernel.ImportPluginFromObject(new MySystemPlugin(cfg), "my_system");
```

3. **Use in tasks**

```json
{
  "taskType": "my-custom-task",
  "payload": { "id": "123" }
}
```

---

## 🌐 Deployment

### **Azure App Service Deployment**

```bash
# 1. Create Azure App Service
az appservice plan create \
  --name azentix-plan \
  --resource-group myResourceGroup \
  --sku B2

az webapp create \
  --resource-group myResourceGroup \
  --plan azentix-plan \
  --name azentix-app

# 2. Configure app settings
az webapp config appsettings set \
  --resource-group myResourceGroup \
  --name azentix-app \
  --settings \
    MODEL_PROVIDER=azure \
    AZURE_OPENAI_ENDPOINT="https://..." \
    AZURE_OPENAI_API_KEY="..." \
    SUPABASE_URL="..." \
    SUPABASE_SERVICE_KEY="..."

# 3. Deploy from GitHub
az webapp deployment source config-zip \
  --resource-group myResourceGroup \
  --name azentix-app \
  --src azentix-app.zip
```

### **Azure Container Instances (ACI)**

```bash
# Build & push image to ACR
az acr build \
  --registry myAcrRegistry \
  --image azentix-host:latest \
  --file src/Azentix.AgentHost/Dockerfile .

# Create container instance
az container create \
  --resource-group myResourceGroup \
  --name azentix-container \
  --image myAcrRegistry.azurecr.io/azentix-host:latest \
  --ports 5000 \
  --environment-variables \
    MODEL_PROVIDER=azure \
    AZURE_OPENAI_ENDPOINT="https://..." \
  --registry-username <username> \
  --registry-password <password>
```

### **Kubernetes (AKS) Deployment**

```yaml
# azentix-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: azentix-host
spec:
  replicas: 3
  selector:
    matchLabels:
      app: azentix-host
  template:
    metadata:
      labels:
        app: azentix-host
    spec:
      containers:
      - name: azentix-host
        image: myregistry.azurecr.io/azentix-host:latest
        ports:
        - containerPort: 5000
        env:
        - name: MODEL_PROVIDER
          value: "azure"
        - name: AZURE_OPENAI_ENDPOINT
          valueFrom:
            secretKeyRef:
              name: azure-openai
              key: endpoint
        livenessProbe:
          httpGet:
            path: /api/agents/status
            port: 5000
          initialDelaySeconds: 30
          periodSeconds: 10
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
```

Deploy:

```bash
kubectl apply -f azentix-deployment.yaml
kubectl expose deployment azentix-host \
  --type=LoadBalancer \
  --port=80 \
  --target-port=5000
```

---

## 🔍 Troubleshooting

### **Issue: "Model provider not configured"**

**Cause:** Neither Azure OpenAI nor Ollama is properly configured.

**Solution:**

```bash
# Check environment variables
printenv | grep -E "AZURE_OPENAI|OLLAMA"

# For Ollama, verify it's running
curl http://localhost:11434/api/tags

# For Azure, verify credentials
curl -H "api-key: $AZURE_OPENAI_API_KEY" \
  $AZURE_OPENAI_ENDPOINT/openai/deployments
```

---

### **Issue: "Supabase connection failed"**

**Cause:** Invalid connection string or network connectivity issue.

**Solution:**

```bash
# Test Supabase connection directly
psql "$SUPABASE_DB_CONNECTION" -c "SELECT VERSION();"

# Check firewall rules (if using cloud)
# Add your IP to Supabase network ACL

# Verify pgvector extension is enabled
psql "$SUPABASE_DB_CONNECTION" -c "SELECT * FROM pg_extension WHERE extname = 'vector';"
```

---

### **Issue: Plugin returns "Unauthorized"**

**Cause:** API credentials are invalid or expired.

**Solution:**

```bash
# SAP
echo "Test SAP connection:"
curl -u $SAP_USERNAME:$SAP_PASSWORD \
  -H "Content-Type: application/json" \
  "$SAP_URL/some-endpoint"

# Salesforce
sf auth list  # Using Salesforce CLI
sf data query list --query "SELECT Id FROM Account LIMIT 1"

# ServiceNow
curl -u $SERVICENOW_USERNAME:$SERVICENOW_PASSWORD \
  "$SERVICENOW_INSTANCE/api/now/incident?limit=1"
```

---

### **Issue: "Agent timeout after N iterations"**

**Cause:** Task is too complex or plugin is hanging.

**Solution:**

```csharp
// In appsettings.json, increase timeout
{
  "AgentConfig": {
    "MaxIterations": 15,
    "TimeoutSeconds": 60
  }
}

// Or check for slow plugins in logs
# Look for plugin execution times > 5s
docker logs azentix-host | grep "plugin" | grep -E "10000ms|[0-9]{5}ms"
```

---

## 🤝 Contributing

We welcome contributions! Here's how:

1. **Fork the repository**

```bash
git clone https://github.com/your-username/azentix-enterprise.git
cd azentix-enterprise
```

2. **Create a feature branch**

```bash
git checkout -b feature/my-feature
```

3. **Make changes & commit**

```bash
git add .
git commit -m "Add: my feature description"
```

4. **Push & create PR**

```bash
git push origin feature/my-feature
```

5. **PR Requirements**
   - Unit tests for new plugins
   - Documentation for new features
   - Update CHANGELOG.md
   - Code follows C# style guide

---

## 📄 License

This project is licensed under the **MIT License** — see [LICENSE](LICENSE) for details.

---

## 🆘 Support & Community

- **Issues**: [GitHub Issues](https://github.com/your-org/azentix-enterprise/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-org/azentix-enterprise/discussions)
- **Email**: support@azentix.dev
- **Slack**: [Join our community](https://azentix.slack.com)

---

## 🎯 Roadmap

- [ ] **v1.1** (Q2 2025): LangGraph full integration
- [ ] **v1.2** (Q3 2025): GraphQL API + WebSocket support
- [ ] **v1.3** (Q4 2025): Fine-tuning support for proprietary models
- [ ] **v2.0** (Q1 2026): Multi-agent consensus & dispute resolution

---

## 📊 Cost Comparison: Azentix vs Azure-Native Stack

| Component | Azure Native | Azentix Stack | Savings |
|-----------|--------------|---------------|---------|
| Service Bus | £50/mo | CloudAMQP | £50 |
| Logic Apps | £100/mo | n8n | £100 |
| APIM | £50/mo | Kong | £50 |
| App Service | £100/mo | Render.com free | £100 |
| Cosmos DB | £50/mo | Supabase free | £50 |
| App Insights | £30/mo | Grafana | £30 |
| **Total** | **£380/mo** | **Azure OpenAI only** | **£360 saved** |

**Note:** Only Azure OpenAI/Foundry is paid. The free tier gives ~$200/month credit — enough for 6+ months of development.

---

## 🙏 Acknowledgments

Built with:
- [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel)
- [Supabase pgvector](https://supabase.com/docs/guides/database/pgvector)
- [RabbitMQ](https://www.rabbitmq.com/)
- [n8n](https://n8n.io/)
- [Kong](https://konghq.com/)
- [Ollama](https://ollama.ai/)

---

**Last Updated:** April 20, 2025  
**Current Version:** 1.0.0

