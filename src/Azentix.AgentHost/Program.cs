
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Instrumentation.Http;
using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Action;
using Azentix.Agents.Plugins;
using Azentix.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ✅ REQUIRED: global HttpClient for Semantic Kernel plugins
builder.Services.AddHttpClient();

// ── Azure OpenAI ──────────────────────────────────────────────────────────
var aoaiEndpoint = cfg["AZURE_OPENAI_ENDPOINT"];
var aoaiKey      = cfg["AZURE_OPENAI_API_KEY"];
var chatDeploy   = cfg["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-5-mini";
// IMPORTANT: embeddings are optional / may not exist
var embedDeploy  = cfg["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"];

var azureOpenAiConfigured =
    !string.IsNullOrWhiteSpace(aoaiEndpoint) &&
    !string.IsNullOrWhiteSpace(aoaiKey);

// ── Semantic Kernel (CHAT ONLY unless embeddings exist) ───────────────────
if (azureOpenAiConfigured)
{
    builder.Services.AddSingleton(sp =>
    {
        var kb = Kernel.CreateBuilder();
        kb.AddAzureOpenAIChatCompletion(chatDeploy, aoaiEndpoint!, aoaiKey!);

        // ✅ Plugins resolved via DI + HttpClient
        kb.Plugins.AddFromType<SapPlugin>("SAP");
        kb.Plugins.AddFromType<SalesforcePlugin>("Salesforce");
        kb.Plugins.AddFromType<ServiceNowPlugin>("ServiceNow");
        kb.Plugins.AddFromType<HubSpotPlugin>("HubSpot");
        kb.Plugins.AddFromType<StripePlugin>("Stripe");
        kb.Plugins.AddFromType<RabbitMQPlugin>("RabbitMQ");
        kb.Plugins.AddFromType<RagPlugin>("RAG");

        return kb.Build();
    });

    // ✅ Embeddings ONLY if deployment exists
    if (!string.IsNullOrWhiteSpace(embedDeploy))
    {
        builder.Services.AddSingleton(_ =>
            new Azure.AI.OpenAI.AzureOpenAIClient(
                new Uri(aoaiEndpoint!), new Azure.AzureKeyCredential(aoaiKey!))
            .GetEmbeddingClient(embedDeploy));
    }
}

// ── Supabase pgvector ─────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new SupabaseConfig
{
    Url                      = cfg["SUPABASE_URL"] ?? "",
    AnonKey                  = cfg["SUPABASE_ANON_KEY"] ?? "",
    ServiceKey               = cfg["SUPABASE_SERVICE_KEY"] ?? "",
    DatabaseConnectionString = cfg["SUPABASE_DB_CONNECTION"] ?? ""
});
builder.Services.AddSingleton<IVectorMemory, SupabaseVectorMemory>();

// ── SAP ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new SapConfiguration
{
    BaseUrl         = cfg["SAP_BASE_URL"] ?? "",
    ApiKey          = cfg["SAP_API_KEY"] ?? "",
    System          = cfg["SAP_SYSTEM"] ?? "SANDBOX",
    DefaultSalesOrg = cfg["SAP_DEFAULT_SALES_ORG"] ?? "GB01"
});

// ── Salesforce ────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new SalesforceConfiguration
{
    InstanceUrl  = cfg["SALESFORCE_INSTANCE_URL"] ?? "",
    ClientId     = cfg["SALESFORCE_CLIENT_ID"] ?? "",
    ClientSecret = cfg["SALESFORCE_CLIENT_SECRET"] ?? "",
    Username     = cfg["SALESFORCE_USERNAME"] ?? "",
    Password     = cfg["SALESFORCE_PASSWORD"] ?? "",
    ApiVersion   = cfg["SALESFORCE_API_VERSION"] ?? "v59.0"
});

// ── ServiceNow ────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new ServiceNowConfiguration
{
    InstanceUrl = cfg["SERVICENOW_INSTANCE_URL"] ?? "",
    Username    = cfg["SERVICENOW_USERNAME"] ?? "",
    Password    = cfg["SERVICENOW_PASSWORD"] ?? ""
});

// ── HubSpot ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new HubSpotConfiguration
{
    AccessToken = cfg["HUBSPOT_ACCESS_TOKEN"] ?? "",
    PortalId    = cfg["HUBSPOT_PORTAL_ID"] ?? "",
    ApiBase     = cfg["HUBSPOT_API_BASE"] ?? "https://api.hubapi.com"
});

// ── Stripe ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new StripeConfiguration
{
    SecretKey     = cfg["STRIPE_SECRET_KEY"] ?? "",
    ApiVersion    = cfg["STRIPE_API_VERSION"] ?? "2024-06-20",
    WebhookSecret = cfg["STRIPE_WEBHOOK_SECRET"]
});

// ── RabbitMQ ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new RabbitMQConfiguration
{
    AmqpUrl            = cfg["CLOUDAMQP_URL"] ?? "",
    QueueSapPrices     = cfg["RABBITMQ_QUEUE_SAP_PRICES"] ?? "sap-price-changes",
    QueueIncidents     = cfg["RABBITMQ_QUEUE_INCIDENTS"] ?? "servicenow-incidents",
    QueueStripe        = cfg["RABBITMQ_QUEUE_STRIPE"] ?? "stripe-events",
    QueueApproval      = cfg["RABBITMQ_QUEUE_APPROVAL"] ?? "approval-queue",
    QueueNotifications = cfg["RABBITMQ_QUEUE_NOTIFICATIONS"] ?? "notifications"
});

// ── Agent configuration ───────────────────────────────────────────────────
builder.Services.AddSingleton(_ => new AgentConfiguration
{
    MaxIterations         = int.Parse(cfg["AGENT_MAX_ITERATIONS"] ?? "10"),
    TimeoutSeconds        = int.Parse(cfg["AGENT_TIMEOUT_SECONDS"] ?? "60"),
    MaxTokensPerIteration = int.Parse(cfg["AGENT_MAX_TOKENS"] ?? "2000"),
    TokenBudget           = int.Parse(cfg["AGENT_TOKEN_BUDGET"] ?? "16000"),
    ModelDeployment       = chatDeploy
});

// ── Agents ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();
builder.Services.AddScoped<IRagAgent, RagAgent>();
builder.Services.AddScoped<IMemoryAgent, MemoryAgent>();
builder.Services.AddScoped<IActionAgent, ActionAgent>();

// ── OpenTelemetry → Grafana ────────────────────────────────────────────────
var grafanaUrl = cfg["GRAFANA_PROMETHEUS_URL"];
if (!string.IsNullOrWhiteSpace(grafanaUrl))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("Azentix.AgentHost"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(grafanaUrl.Replace("/api/prom", "") + "/otlp");
                o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                o.Headers  = "Authorization=Basic " +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        $"{cfg["GRAFANA_PROMETHEUS_USER"]}:{cfg["GRAFANA_API_KEY"]}"));
            }));
}

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck(
        "supabase-pgvector",
        () => string.IsNullOrWhiteSpace(cfg["SUPABASE_DB_CONNECTION"])
            ? HealthCheckResult.Degraded("SUPABASE_DB_CONNECTION not configured")
            : HealthCheckResult.Healthy(),
        tags: new[] { "db", "vector" })
    .AddCheck(
        "azure-openai",
        () => azureOpenAiConfigured
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Azure OpenAI not configured"),
        tags: new[] { "ai" });

// ── Web API ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Azentix Enterprise Agent API",
        Version     = "v1",
        Description = "SAP · Salesforce · ServiceNow · HubSpot · Stripe — Free Stack"
    });
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => new
{
    name = "Azentix Agent Host",
    version = "1.0.0",
    aiEnabled = azureOpenAiConfigured,
    docs = "/swagger"
});
app.Run();

public partial class Program { }
