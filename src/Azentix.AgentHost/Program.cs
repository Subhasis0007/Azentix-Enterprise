
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Plugins;
using Azentix.Models;

using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

//
// ─────────────────────────────────────────────────────────────
// ✅ ASP.NET Core DI (Infrastructure)
// ─────────────────────────────────────────────────────────────
//
builder.Services.AddHttpClient(); // for controllers, health checks, etc.

//
// ─────────────────────────────────────────────────────────────
// ✅ Azure OpenAI Configuration
// ─────────────────────────────────────────────────────────────
//
var aoaiEndpoint = cfg["AZURE_OPENAI_ENDPOINT"];
var aoaiKey      = cfg["AZURE_OPENAI_API_KEY"];
var chatDeploy   = cfg["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";
var embedDeploy  = cfg["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";

var azureConfigured =
    !string.IsNullOrWhiteSpace(aoaiEndpoint) &&
    !string.IsNullOrWhiteSpace(aoaiKey);

//
// ─────────────────────────────────────────────────────────────
// ✅ SEMANTIC KERNEL (PLUGIN-SAFE DI)
// ─────────────────────────────────────────────────────────────
//
if (azureConfigured)
{
    builder.Services.AddSingleton<Kernel>(_ =>
    {
        var kb = Kernel.CreateBuilder();

        // ✅✅✅ THIS IS THE CRITICAL FIX
        // Register HttpClientFactory INSIDE Semantic Kernel DI
        kb.Services.AddHttpClient();

        // ---------------------------------------------------------
        // Azure OpenAI client (correct credential type)
        // ---------------------------------------------------------
        var azureClient = new AzureOpenAIClient(
            new Uri(aoaiEndpoint!),
            new ApiKeyCredential(aoaiKey!)
        );

        kb.Services.AddSingleton(azureClient);

        // ---------------------------------------------------------
        // Azure OpenAI Chat (explicit, no fallback)
        // ---------------------------------------------------------
        var chatService = new AzureOpenAIChatCompletionService(
            chatDeploy,
            azureClient,
            null,
            null
        );

        kb.Services.AddSingleton<IChatCompletionService>(chatService);

        // ---------------------------------------------------------
        // Azure OpenAI Embeddings
        // ---------------------------------------------------------
        kb.Services.AddSingleton<EmbeddingClient>(
            azureClient.GetEmbeddingClient(embedDeploy)
        );

        // ---------------------------------------------------------
        // Vector Memory (RAG)
        // ---------------------------------------------------------
        kb.Services.AddSingleton(new SupabaseConfig
        {
            Url                      = cfg["SUPABASE_URL"] ?? "",
            AnonKey                  = cfg["SUPABASE_ANON_KEY"] ?? "",
            ServiceKey               = cfg["SUPABASE_SERVICE_KEY"] ?? "",
            DatabaseConnectionString = cfg["SUPABASE_DB_CONNECTION"] ?? ""
        });

        kb.Services.AddSingleton<IVectorMemory, SupabaseVectorMemory>();
        kb.Services.AddScoped<IRagAgent, RagAgent>();

        // ---------------------------------------------------------
        // Plugins (now IHttpClientFactory is resolvable ✅)
        // ---------------------------------------------------------
        kb.Plugins.AddFromType<SapPlugin>("SAP");
        kb.Plugins.AddFromType<SalesforcePlugin>("Salesforce");
        kb.Plugins.AddFromType<ServiceNowPlugin>("ServiceNow");
        kb.Plugins.AddFromType<HubSpotPlugin>("HubSpot");
        kb.Plugins.AddFromType<StripePlugin>("Stripe");
        kb.Plugins.AddFromType<RabbitMQPlugin>("RabbitMQ");
        kb.Plugins.AddFromType<RagPlugin>("RAG");

        return kb.Build();
    });
}

//
// ─────────────────────────────────────────────────────────────
// ✅ ASP.NET Core DI — Controllers
// ─────────────────────────────────────────────────────────────
//
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();

builder.Services.AddSingleton(new AgentConfiguration
{
    MaxIterations   = int.Parse(cfg["AGENT_MAX_ITERATIONS"] ?? "10"),
    TimeoutSeconds  = int.Parse(cfg["AGENT_TIMEOUT_SECONDS"] ?? "60"),
    TokenBudget     = int.Parse(cfg["AGENT_TOKEN_BUDGET"] ?? "16000"),
    ModelDeployment = chatDeploy
});

//
// ─────────────────────────────────────────────────────────────
// ✅ ASP.NET Pipeline
// ─────────────────────────────────────────────────────────────
//
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck(
        "azure-openai",
        () => azureConfigured
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Azure OpenAI not configured")
    );

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.MapGet("/", () => new
{
    name = "Azentix Agent Host",
    aiEnabled = azureConfigured,
    docs = "/swagger"
});

app.Run();

public partial class Program { }
