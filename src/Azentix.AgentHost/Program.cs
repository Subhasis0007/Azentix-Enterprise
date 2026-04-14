
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Plugins;
using Azentix.Models;

using Azure;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ ASP.NET Core DI (Infrastructure)
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddHttpClient();

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ Azure OpenAI Configuration
// ─────────────────────────────────────────────────────────────────────────────
//
var aoaiEndpoint  = cfg["AZURE_OPENAI_ENDPOINT"];
var aoaiKey       = cfg["AZURE_OPENAI_API_KEY"];
var chatDeploy    = cfg["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";
var embedDeploy   = cfg["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";

var azureOpenAiConfigured =
    !string.IsNullOrWhiteSpace(aoaiEndpoint) &&
    !string.IsNullOrWhiteSpace(aoaiKey);

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ SEMANTIC KERNEL (EXPLICIT AZURE BINDING – FINAL FIX)
// ─────────────────────────────────────────────────────────────────────────────
//
if (azureOpenAiConfigured)
{
    builder.Services.AddSingleton<Kernel>(_ =>
    {
        var kernelBuilder = Kernel.CreateBuilder();

        // ---------------------------------------------------------------------
        // ✅ HttpClients for Plugins
        // ---------------------------------------------------------------------
        kernelBuilder.Services.AddHttpClient("SAP");
        kernelBuilder.Services.AddHttpClient("Salesforce");
        kernelBuilder.Services.AddHttpClient("ServiceNow");
        kernelBuilder.Services.AddHttpClient("HubSpot");
        kernelBuilder.Services.AddHttpClient("Stripe");
        kernelBuilder.Services.AddHttpClient("RabbitMQ");

        // ---------------------------------------------------------------------
        // ✅ Plugin Configurations
        // ---------------------------------------------------------------------
        kernelBuilder.Services.AddSingleton(new SapConfiguration
        {
            BaseUrl         = cfg["SAP_BASE_URL"] ?? "",
            ApiKey          = cfg["SAP_API_KEY"] ?? "",
            System          = cfg["SAP_SYSTEM"] ?? "SANDBOX",
            DefaultSalesOrg = cfg["SAP_DEFAULT_SALES_ORG"] ?? "GB01"
        });

        kernelBuilder.Services.AddSingleton(new SalesforceConfiguration
        {
            InstanceUrl  = cfg["SALESFORCE_INSTANCE_URL"] ?? "",
            ClientId     = cfg["SALESFORCE_CLIENT_ID"] ?? "",
            ClientSecret = cfg["SALESFORCE_CLIENT_SECRET"] ?? "",
            Username     = cfg["SALESFORCE_USERNAME"] ?? "",
            Password     = cfg["SALESFORCE_PASSWORD"] ?? ""
        });

        kernelBuilder.Services.AddSingleton(new ServiceNowConfiguration
        {
            InstanceUrl = cfg["SERVICENOW_INSTANCE_URL"] ?? "",
            Username    = cfg["SERVICENOW_USERNAME"] ?? "",
            Password    = cfg["SERVICENOW_PASSWORD"] ?? ""
        });

        kernelBuilder.Services.AddSingleton(new HubSpotConfiguration
        {
            AccessToken = cfg["HUBSPOT_ACCESS_TOKEN"] ?? "",
            PortalId    = cfg["HUBSPOT_PORTAL_ID"] ?? "",
            ApiBase     = cfg["HUBSPOT_API_BASE"] ?? "https://api.hubapi.com"
        });

        kernelBuilder.Services.AddSingleton(new StripeConfiguration
        {
            SecretKey = cfg["STRIPE_SECRET_KEY"] ?? ""
        });

        kernelBuilder.Services.AddSingleton(new RabbitMQConfiguration
        {
            AmqpUrl = cfg["CLOUDAMQP_URL"] ?? ""
        });

        // ---------------------------------------------------------------------
        // ✅ Supabase Vector Memory (RAG)
        // ---------------------------------------------------------------------
        kernelBuilder.Services.AddSingleton(new SupabaseConfig
        {
            Url                      = cfg["SUPABASE_URL"] ?? "",
            AnonKey                  = cfg["SUPABASE_ANON_KEY"] ?? "",
            ServiceKey               = cfg["SUPABASE_SERVICE_KEY"] ?? "",
            DatabaseConnectionString = cfg["SUPABASE_DB_CONNECTION"] ?? ""
        });

        kernelBuilder.Services.AddSingleton<IVectorMemory, SupabaseVectorMemory>();
        kernelBuilder.Services.AddScoped<IRagAgent, RagAgent>();

        // ---------------------------------------------------------------------
        // ✅ Azure OpenAI Client (MANUAL – NO SK FACTORY)
        // ---------------------------------------------------------------------
        var azureClient = new AzureOpenAIClient(
            new Uri(aoaiEndpoint!),
            new AzureKeyCredential(aoaiKey!)
        );

        kernelBuilder.Services.AddSingleton(azureClient);

        kernelBuilder.Services.AddSingleton<EmbeddingClient>(_ =>
            azureClient.GetEmbeddingClient(embedDeploy)
        );

        // ✅ EXPLICIT Azure Chat binding (NO OpenAI fallback)
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: chatDeploy,
            azureOpenAIClient: azureClient
        );

        // ---------------------------------------------------------------------
        // ✅ Plugins
        // ---------------------------------------------------------------------
        kernelBuilder.Plugins.AddFromType<SapPlugin>("SAP");
        kernelBuilder.Plugins.AddFromType<SalesforcePlugin>("Salesforce");
        kernelBuilder.Plugins.AddFromType<ServiceNowPlugin>("ServiceNow");
        kernelBuilder.Plugins.AddFromType<HubSpotPlugin>("HubSpot");
        kernelBuilder.Plugins.AddFromType<StripePlugin>("Stripe");
        kernelBuilder.Plugins.AddFromType<RabbitMQPlugin>("RabbitMQ");
        kernelBuilder.Plugins.AddFromType<RagPlugin>("RAG");

        return kernelBuilder.Build();
    });
}

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ ASP.NET Core DI — Controller Agents
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();

builder.Services.AddSingleton(new AgentConfiguration
{
    MaxIterations         = int.Parse(cfg["AGENT_MAX_ITERATIONS"] ?? "10"),
    TimeoutSeconds        = int.Parse(cfg["AGENT_TIMEOUT_SECONDS"] ?? "60"),
    MaxTokensPerIteration = int.Parse(cfg["AGENT_MAX_TOKENS"] ?? "2000"),
    TokenBudget           = int.Parse(cfg["AGENT_TOKEN_BUDGET"] ?? "16000"),
    ModelDeployment       = chatDeploy
});

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ ASP.NET Pipeline
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck(
        "azure-openai",
        () => azureOpenAiConfigured
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
    aiEnabled = azureOpenAiConfigured,
    docs = "/swagger"
});

app.Run();

public partial class Program { }
