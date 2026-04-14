
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
builder.Services.AddHttpClient();

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

        // ---------------------------------------------------------
        // ✅ HttpClientFactory MUST be registered in SK DI
        // ---------------------------------------------------------
        kb.Services.AddHttpClient();

        // ---------------------------------------------------------
        // ✅ Plugin Configurations (CRITICAL FIX)
        // ---------------------------------------------------------
        kb.Services.AddSingleton(new SapConfiguration
        {
            BaseUrl         = cfg["SAP_BASE_URL"] ?? "",
            ApiKey          = cfg["SAP_API_KEY"] ?? "",
            System          = cfg["SAP_SYSTEM"] ?? "",
            DefaultSalesOrg = cfg["SAP_DEFAULT_SALES_ORG"] ?? ""
        });

        kb.Services.AddSingleton(new SalesforceConfiguration
        {
            InstanceUrl  = cfg["SALESFORCE_INSTANCE_URL"] ?? "",
            ClientId     = cfg["SALESFORCE_CLIENT_ID"] ?? "",
            ClientSecret = cfg["SALESFORCE_CLIENT_SECRET"] ?? "",
            Username     = cfg["SALESFORCE_USERNAME"] ?? "",
            Password     = cfg["SALESFORCE_PASSWORD"] ?? ""
        });

        kb.Services.AddSingleton(new ServiceNowConfiguration
        {
            InstanceUrl = cfg["SERVICENOW_INSTANCE_URL"] ?? "",
            Username    = cfg["SERVICENOW_USERNAME"] ?? "",
            Password    = cfg["SERVICENOW_PASSWORD"] ?? ""
        });

        kb.Services.AddSingleton(new HubSpotConfiguration
        {
            AccessToken = cfg["HUBSPOT_ACCESS_TOKEN"] ?? "",
            PortalId    = cfg["HUBSPOT_PORTAL_ID"] ?? "",
            ApiBase     = cfg["HUBSPOT_API_BASE"] ?? ""
        });

        kb.Services.AddSingleton(new StripeConfiguration
        {
            SecretKey = cfg["STRIPE_SECRET_KEY"] ?? ""
        });

        kb.Services.AddSingleton(new RabbitMQConfiguration
        {
            AmqpUrl = cfg["CLOUDAMQP_URL"] ?? ""
        });

        // ---------------------------------------------------------
        // ✅ Azure OpenAI Client (correct credential)
        // ---------------------------------------------------------
        var azureClient = new AzureOpenAIClient(
            new Uri(aoaiEndpoint!),
            new ApiKeyCredential(aoaiKey!)
        );

        kb.Services.AddSingleton(azureClient);

        var chatService = new AzureOpenAIChatCompletionService(
            chatDeploy,
            azureClient,
            null,
            null
        );

        kb.Services.AddSingleton<IChatCompletionService>(chatService);

        kb.Services.AddSingleton<EmbeddingClient>(
            azureClient.GetEmbeddingClient(embedDeploy)
        );

        // ---------------------------------------------------------
        // ✅ Vector Memory (RAG)
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
        // ✅ Plugins (all dependencies now resolvable)
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
