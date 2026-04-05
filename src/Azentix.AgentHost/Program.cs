using Microsoft.SemanticKernel;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Action;
using Azentix.Agents.Plugins;
using Azentix.Models;

var builder = WebApplication.CreateBuilder(args);

// ■■ READ CONFIGURATION (injected by Doppler or env vars) ■■
var aoaiEndpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]!;
var aoaiKey      = builder.Configuration["AZURE_OPENAI_API_KEY"]!;
var deployment   = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";
var embDeploy    = builder.Configuration["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-small";

// ■■ SEMANTIC KERNEL — all 5 system plugins registered as tools ■■
builder.Services.AddSingleton(sp =>
{
    var kb = Kernel.CreateBuilder();
    kb.AddAzureOpenAIChatCompletion(deployment, aoaiEndpoint, aoaiKey);
    kb.Plugins.AddFromType<SapPlugin>("SAP");
    kb.Plugins.AddFromType<SalesforcePlugin>("Salesforce");
    kb.Plugins.AddFromType<ServiceNowPlugin>("ServiceNow");
    kb.Plugins.AddFromType<HubSpotPlugin>("HubSpot");
    kb.Plugins.AddFromType<StripePlugin>("Stripe");
    kb.Plugins.AddFromType<RabbitMQPlugin>("RabbitMQ");
    kb.Plugins.AddFromType<RagPlugin>("RAG");
    return kb.Build();
});

// ■■ SUPABASE VECTOR MEMORY (replaces Cosmos DB) ■■
builder.Services.AddSingleton<SupabaseConfig>(_ => new SupabaseConfig {
    Url = builder.Configuration["SUPABASE_URL"]!,
    AnonKey = builder.Configuration["SUPABASE_ANON_KEY"]!,
    ServiceKey = builder.Configuration["SUPABASE_SERVICE_KEY"]!,
    DatabaseConnectionString = builder.Configuration["SUPABASE_DB_CONNECTION"]!
});
builder.Services.AddSingleton<IVectorMemory, SupabaseVectorMemory>();

// ■■ AZURE OPENAI EMBEDDING CLIENT ■■
builder.Services.AddSingleton(_ =>
    new Azure.AI.OpenAI.AzureOpenAIClient(
        new Uri(aoaiEndpoint), new Azure.AzureKeyCredential(aoaiKey))
        .GetEmbeddingClient(embDeploy));

// ■■ SAP ■■
builder.Services.AddSingleton<SapConfiguration>(_ => new SapConfiguration {
    BaseUrl = builder.Configuration["SAP_BASE_URL"]!,
    ApiKey  = builder.Configuration["SAP_API_KEY"]!,
    System  = builder.Configuration["SAP_SYSTEM"] ?? "SANDBOX",
    DefaultSalesOrg = builder.Configuration["SAP_DEFAULT_SALES_ORG"] ?? "GB01"
});
builder.Services.AddHttpClient<SapPlugin>(c => c.Timeout = TimeSpan.FromSeconds(30));

// ■■ SALESFORCE ■■
builder.Services.AddSingleton<SalesforceConfiguration>(_ => new SalesforceConfiguration {
    InstanceUrl  = builder.Configuration["SALESFORCE_INSTANCE_URL"]!,
    ClientId     = builder.Configuration["SALESFORCE_CLIENT_ID"]!,
    ClientSecret = builder.Configuration["SALESFORCE_CLIENT_SECRET"]!,
    Username     = builder.Configuration["SALESFORCE_USERNAME"]!,
    Password     = builder.Configuration["SALESFORCE_PASSWORD"]!,
    ApiVersion   = builder.Configuration["SALESFORCE_API_VERSION"] ?? "v59.0"
});
builder.Services.AddHttpClient<SalesforcePlugin>();

// ■■ SERVICENOW ■■
builder.Services.AddSingleton<ServiceNowConfiguration>(_ => new ServiceNowConfiguration {
    InstanceUrl = builder.Configuration["SERVICENOW_INSTANCE_URL"]!,
    Username    = builder.Configuration["SERVICENOW_USERNAME"]!,
    Password    = builder.Configuration["SERVICENOW_PASSWORD"]!
});
builder.Services.AddHttpClient<ServiceNowPlugin>();

// ■■ HUBSPOT ■■
builder.Services.AddSingleton<HubSpotConfiguration>(_ => new HubSpotConfiguration {
    AccessToken = builder.Configuration["HUBSPOT_ACCESS_TOKEN"]!,
    PortalId    = builder.Configuration["HUBSPOT_PORTAL_ID"]!,
    ApiBase     = builder.Configuration["HUBSPOT_API_BASE"] ?? "https://api.hubapi.com"
});
builder.Services.AddHttpClient<HubSpotPlugin>();

// ■■ STRIPE ■■
builder.Services.AddSingleton<StripeConfiguration>(_ => new StripeConfiguration {
    SecretKey     = builder.Configuration["STRIPE_SECRET_KEY"]!,
    ApiVersion    = builder.Configuration["STRIPE_API_VERSION"] ?? "2024-06-20",
    WebhookSecret = builder.Configuration["STRIPE_WEBHOOK_SECRET"]
});
builder.Services.AddHttpClient<StripePlugin>();

// ■■ RABBITMQ (replaces Azure Service Bus) ■■
builder.Services.AddSingleton<RabbitMQConfiguration>(_ => new RabbitMQConfiguration {
    AmqpUrl           = builder.Configuration["CLOUDAMQP_URL"]!,
    QueueSapPrices    = builder.Configuration["RABBITMQ_QUEUE_SAP_PRICES"]    ?? "sap-price-changes",
    QueueIncidents    = builder.Configuration["RABBITMQ_QUEUE_INCIDENTS"]     ?? "servicenow-incidents",
    QueueStripe       = builder.Configuration["RABBITMQ_QUEUE_STRIPE"]        ?? "stripe-events",
    QueueApproval     = builder.Configuration["RABBITMQ_QUEUE_APPROVAL"]      ?? "approval-queue",
    QueueNotifications= builder.Configuration["RABBITMQ_QUEUE_NOTIFICATIONS"] ?? "notifications"
});

// ■■ AGENT CONFIGURATION ■■
builder.Services.AddSingleton<AgentConfiguration>(_ => new AgentConfiguration {
    MaxIterations        = int.Parse(builder.Configuration["AGENT_MAX_ITERATIONS"] ?? "10"),
    TimeoutSeconds       = int.Parse(builder.Configuration["AGENT_TIMEOUT_SECONDS"] ?? "60"),
    MaxTokensPerIteration= int.Parse(builder.Configuration["AGENT_MAX_TOKENS"] ?? "2000"),
    TokenBudget          = int.Parse(builder.Configuration["AGENT_TOKEN_BUDGET"] ?? "16000"),
    ModelDeployment      = deployment
});

// ■■ AGENTS ■■
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();
builder.Services.AddScoped<IRagAgent, RagAgent>();
builder.Services.AddScoped<IMemoryAgent, MemoryAgent>();
builder.Services.AddScoped<IActionAgent, ActionAgent>();

// ■■ OPENTELEMETRY -> Grafana Cloud ■■
var grafanaOtlpUrl = builder.Configuration["GRAFANA_PROMETHEUS_URL"] ?? "";
if (!string.IsNullOrEmpty(grafanaOtlpUrl))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("Azentix.AgentHost"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => {
                o.Endpoint = new Uri(grafanaOtlpUrl.Replace("/api/prom", "") + "/otlp");
                o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                o.Headers  = "Authorization=Basic " + Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        builder.Configuration["GRAFANA_PROMETHEUS_USER"] + ":" +
                        builder.Configuration["GRAFANA_API_KEY"]));
            }));
}

// ■■ HEALTH CHECKS ■■
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration["SUPABASE_DB_CONNECTION"] ?? "Host=localhost",
        name: "supabase-pgvector",
        tags: new[] { "db", "vector" })
    .AddUrlGroup(
        new Uri(aoaiEndpoint + "openai/deployments"),
        name: "azure-openai",
        tags: new[] { "ai" });

// ■■ API ■■
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() {
    Title       = "Azentix Enterprise Agent API",
    Version     = "v1",
    Description = "SAP + Salesforce + ServiceNow + HubSpot + Stripe | Free Stack Edition" }));

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
