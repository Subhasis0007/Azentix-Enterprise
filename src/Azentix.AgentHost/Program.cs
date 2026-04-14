
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Azentix.Agents.Director;
using Azentix.Agents.Rag;
using Azentix.Agents.Memory;
using Azentix.Agents.Action;
using Azentix.Agents.Plugins;
using Azentix.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

//
// ─────────────────────────────────────────────────────────────────────────────
// ASP.NET CORE HTTP CLIENT (for controllers, etc.)
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddHttpClient();

//
// ─────────────────────────────────────────────────────────────────────────────
// Azure OpenAI configuration
// ─────────────────────────────────────────────────────────────────────────────
//
var aoaiEndpoint = cfg["AZURE_OPENAI_ENDPOINT"];
var aoaiKey      = cfg["AZURE_OPENAI_API_KEY"];
var chatDeploy   = cfg["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";

var azureOpenAiConfigured =
    !string.IsNullOrWhiteSpace(aoaiEndpoint) &&
    !string.IsNullOrWhiteSpace(aoaiKey);

//
// ─────────────────────────────────────────────────────────────────────────────
// ✅ SEMANTIC KERNEL (SUPPORTED DI REGISTRATION)
// ─────────────────────────────────────────────────────────────────────────────
//
if (azureOpenAiConfigured)
{
    builder.Services.AddSingleton<Kernel>(_ =>
    {
        var kernelBuilder = Kernel.CreateBuilder();

        // ✅ Register HttpClients INSIDE Semantic Kernel
        kernelBuilder.Services.AddHttpClient("SAP");
        kernelBuilder.Services.AddHttpClient("Salesforce");
        kernelBuilder.Services.AddHttpClient("ServiceNow");
        kernelBuilder.Services.AddHttpClient("HubSpot");
        kernelBuilder.Services.AddHttpClient("Stripe");
        kernelBuilder.Services.AddHttpClient("RabbitMQ");

        // ✅ Register configuration objects INSIDE Semantic Kernel
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

        // ✅ Azure OpenAI
        kernelBuilder.AddAzureOpenAIChatCompletion(
            chatDeploy,
            aoaiEndpoint!,
            aoaiKey!
        );

        // ✅ Plugins
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
// Configuration objects
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddSingleton(new SapConfiguration
{
    BaseUrl = cfg["SAP_BASE_URL"] ?? "",
    ApiKey  = cfg["SAP_API_KEY"] ?? ""
});

builder.Services.AddSingleton(new SalesforceConfiguration
{
    InstanceUrl  = cfg["SALESFORCE_INSTANCE_URL"] ?? "",
    ClientId     = cfg["SALESFORCE_CLIENT_ID"] ?? "",
    ClientSecret = cfg["SALESFORCE_CLIENT_SECRET"] ?? "",
    Username     = cfg["SALESFORCE_USERNAME"] ?? "",
    Password     = cfg["SALESFORCE_PASSWORD"] ?? ""
});

builder.Services.AddSingleton(new ServiceNowConfiguration
{
    InstanceUrl = cfg["SERVICENOW_INSTANCE_URL"] ?? "",
    Username    = cfg["SERVICENOW_USERNAME"] ?? "",
    Password    = cfg["SERVICENOW_PASSWORD"] ?? ""
});


builder.Services.AddSingleton(new HubSpotConfiguration
{
    AccessToken = cfg["HUBSPOT_ACCESS_TOKEN"] ?? "",
    PortalId    = cfg["HUBSPOT_PORTAL_ID"] ?? "",
    ApiBase     = cfg["HUBSPOT_API_BASE"] ?? "https://api.hubapi.com"
});


builder.Services.AddSingleton(new StripeConfiguration
{
    SecretKey = cfg["STRIPE_SECRET_KEY"] ?? ""
});

builder.Services.AddSingleton(new RabbitMQConfiguration
{
    AmqpUrl = cfg["CLOUDAMQP_URL"] ?? ""
});

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
// Agents
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddScoped<IDirectorAgent, DirectorAgent>();
builder.Services.AddScoped<IRagAgent, RagAgent>();
builder.Services.AddScoped<IMemoryAgent, MemoryAgent>();
builder.Services.AddScoped<IActionAgent, ActionAgent>();

//
// ─────────────────────────────────────────────────────────────────────────────
// Web + Health
// ─────────────────────────────────────────────────────────────────────────────
//
builder.Services.AddHealthChecks()
    .AddCheck("azure-openai",
        () => azureOpenAiConfigured
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Azure OpenAI not configured"));

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

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
