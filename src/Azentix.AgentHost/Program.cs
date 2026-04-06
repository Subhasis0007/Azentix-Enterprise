using System.Text;
using Microsoft.SemanticKernel;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Action;
using Azentix.Agents.Plugins;
using Azentix.Models;
using OpenTelemetry.Instrumentation.Http;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;

// ── Azure OpenAI ──────────────────────────────────────────────────────────
var aoaiEndpoint = cfg["AZURE_OPENAI_ENDPOINT"]   ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT missing");
var aoaiKey      = cfg["AZURE_OPENAI_API_KEY"]    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY missing");
var chatDeploy   = cfg["AZURE_OPENAI_DEPLOYMENT_NAME"]      ?? "gpt-4o-mini";
var embedDeploy  = cfg["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";

// ── Semantic Kernel — all 7 plugins registered as auto-invokable tools ────
builder.Services.AddSingleton(sp =>
{
    var kb = Kernel.CreateBuilder();
    kb.AddAzureOpenAIChatCompletion(chatDeploy, aoaiEndpoint, aoaiKey);
    kb.Plugins.AddFromType<SapPlugin>("SAP");
    kb.Plugins.AddFromType<SalesforcePlugin>("Salesforce");
    kb.Plugins.AddFromType<ServiceNowPlugin>("ServiceNow");
    kb.Plugins.AddFromType<HubSpotPlugin>("HubSpot");
    kb.Plugins.AddFromType<StripePlugin>("Stripe");
    kb.Plugins.AddFromType<RabbitMQPlugin>("RabbitMQ");
    kb.Plugins.AddFromType<RagPlugin>("RAG");
    return kb.Build();
});

// ── Supabase pgvector (replaces Cosmos DB) ────────────────────────────────
builder.Services.AddSingleton<SupabaseConfig>(_ => new SupabaseConfig {
    Url                      = cfg["SUPABASE_URL"]         ?? "",
    AnonKey                  = cfg["SUPABASE_ANON_KEY"]    ?? "",
    ServiceKey               = cfg["SUPABASE_SERVICE_KEY"] ?? "",
    DatabaseConnectionString = cfg["SUPABASE_DB_CONNECTION"] ?? ""
});
builder.Services.AddSingleton<IVectorMemory, SupabaseVectorMemory>();

// ── Azure OpenAI Embedding client ─────────────────────────────────────────
builder.Services.AddSingleton(_ =>
    new Azure.AI.OpenAI.AzureOpenAIClient(
        new Uri(aoaiEndpoint), new Azure.AzureKeyCredential(aoaiKey))
    .GetEmbeddingClient(embedDeploy));

// ── SAP ───────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SapConfiguration>(_ => new SapConfiguration {
    BaseUrl         = cfg["SAP_BASE_URL"] ?? "",
    ApiKey          = cfg["SAP_API_KEY"]  ?? "",
    System          = cfg["SAP_SYSTEM"]           ?? "SANDBOX",
    DefaultSalesOrg = cfg["SAP_DEFAULT_SALES_ORG"] ?? "GB01"
});
builder.Services.AddHttpClient<SapPlugin>(c => c.Timeout = TimeSpan.FromSeconds(30));

// ── Salesforce ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SalesforceConfiguration>(_ => new SalesforceConfiguration {
    InstanceUrl  = cfg["SALESFORCE_INSTANCE_URL"]  ?? "",
    ClientId     = cfg["SALESFORCE_CLIENT_ID"]     ?? "",
    ClientSecret = cfg["SALESFORCE_CLIENT_SECRET"] ?? "",
    Username     = cfg["SALESFORCE_USERNAME"]      ?? "",
    Password     = cfg["SALESFORCE_PASSWORD"]      ?? "",
    ApiVersion   = cfg["SALESFORCE_API_VERSION"]   ?? "v59.0"
});
builder.Services.AddHttpClient<SalesforcePlugin>();

// ── ServiceNow ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ServiceNowConfiguration>(_ => new ServiceNowConfiguration {
    InstanceUrl = cfg["SERVICENOW_INSTANCE_URL"] ?? "",
    Username    = cfg["SERVICENOW_USERNAME"]     ?? "",
    Password    = cfg["SERVICENOW_PASSWORD"]     ?? ""
});
builder.Services.AddHttpClient<ServiceNowPlugin>();

// ── HubSpot ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton<HubSpotConfiguration>(_ => new HubSpotConfiguration {
    AccessToken = cfg["HUBSPOT_ACCESS_TOKEN"] ?? "",
    PortalId    = cfg["HUBSPOT_PORTAL_ID"]   ?? "",
    ApiBase     = cfg["HUBSPOT_API_BASE"]    ?? "https://api.hubapi.com"
});
builder.Services.AddHttpClient<HubSpotPlugin>();

// ── Stripe ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<StripeConfiguration>(_ => new StripeConfiguration {
    SecretKey     = cfg["STRIPE_SECRET_KEY"]     ?? "",
    ApiVersion    = cfg["STRIPE_API_VERSION"]    ?? "2024-06-20",
    WebhookSecret = cfg["STRIPE_WEBHOOK_SECRET"]
});
builder.Services.AddHttpClient<StripePlugin>();

// ── RabbitMQ (replaces Azure Service Bus) ─────────────────────────────────
builder.Services.AddSingleton<RabbitMQConfiguration>(_ => new RabbitMQConfiguration {
    AmqpUrl            = cfg["CLOUDAMQP_URL"]                   ?? "",
    QueueSapPrices     = cfg["RABBITMQ_QUEUE_SAP_PRICES"]       ?? "sap-price-changes",
    QueueIncidents     = cfg["RABBITMQ_QUEUE_INCIDENTS"]        ?? "servicenow-incidents",
    QueueStripe        = cfg["RABBITMQ_QUEUE_STRIPE"]           ?? "stripe-events",
    QueueApproval      = cfg["RABBITMQ_QUEUE_APPROVAL"]         ?? "approval-queue",
    QueueNotifications = cfg["RABBITMQ_QUEUE_NOTIFICATIONS"]    ?? "notifications"
});

// ── Agent configuration ───────────────────────────────────────────────────
builder.Services.AddSingleton<AgentConfiguration>(_ => new AgentConfiguration {
    MaxIterations         = int.Parse(cfg["AGENT_MAX_ITERATIONS"] ?? "10"),
    TimeoutSeconds        = int.Parse(cfg["AGENT_TIMEOUT_SECONDS"] ?? "60"),
    MaxTokensPerIteration = int.Parse(cfg["AGENT_MAX_TOKENS"]      ?? "2000"),
    TokenBudget           = int.Parse(cfg["AGENT_TOKEN_BUDGET"]    ?? "16000"),
    ModelDeployment       = chatDeploy
});

// ── Agents (scoped: fresh instance per HTTP request) ─────────────────────
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();
builder.Services.AddScoped<IRagAgent,      RagAgent>();
builder.Services.AddScoped<IMemoryAgent,   MemoryAgent>();
builder.Services.AddScoped<IActionAgent,   ActionAgent>();

// ── OpenTelemetry → Grafana Cloud (replaces App Insights) ─────────────────
var grafanaUrl = cfg["GRAFANA_PROMETHEUS_URL"] ?? "";
if (!string.IsNullOrEmpty(grafanaUrl))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("Azentix.AgentHost"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => {
                o.Endpoint = new Uri(grafanaUrl.Replace("/api/prom", "") + "/otlp");
                o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                o.Headers  = "Authorization=Basic " + Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        $"{cfg["GRAFANA_PROMETHEUS_USER"]}:{cfg["GRAFANA_API_KEY"]}"));
            }));
}

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(cfg["SUPABASE_DB_CONNECTION"] ?? "Host=localhost",
               name: "supabase-pgvector", tags: ["db", "vector"])
    .AddUrlGroup(new Uri(aoaiEndpoint + "openai/deployments"),
                 name: "azure-openai",    tags: ["ai"]);

// ── Web API ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() {
    Title       = "Azentix Enterprise Agent API",
    Version     = "v1",
    Description = "SAP · Salesforce · ServiceNow · HubSpot · Stripe — Free Stack" }));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => new {
    name    = "Azentix Agent Host",
    version = "1.0.0",
    stack   = "Supabase + CloudAMQP + Kong + n8n + Render + Doppler + Grafana",
    docs    = "/swagger"
});
app.Run();

public partial class Program { }
