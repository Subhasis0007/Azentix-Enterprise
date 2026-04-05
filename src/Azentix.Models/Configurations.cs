namespace Azentix.Models;

public record SupabaseConfig
{
    public required string Url                     { get; init; }
    public required string AnonKey                 { get; init; }
    public required string ServiceKey              { get; init; }
    public required string DatabaseConnectionString { get; init; }
}

public record SapConfiguration
{
    public required string BaseUrl        { get; init; }
    public required string ApiKey         { get; init; }
    public string System                  { get; init; } = "SANDBOX";
    public string DefaultSalesOrg         { get; init; } = "GB01";
}

public record SalesforceConfiguration
{
    public required string InstanceUrl    { get; init; }
    public required string ClientId       { get; init; }
    public required string ClientSecret   { get; init; }
    public required string Username       { get; init; }
    public required string Password       { get; init; }
    public string ApiVersion              { get; init; } = "v59.0";
}

public record ServiceNowConfiguration
{
    public required string InstanceUrl    { get; init; }
    public required string Username       { get; init; }
    public required string Password       { get; init; }
}

public record HubSpotConfiguration
{
    public required string AccessToken    { get; init; }
    public required string PortalId       { get; init; }
    public string ApiBase                 { get; init; } = "https://api.hubapi.com";
}

public record StripeConfiguration
{
    public required string SecretKey      { get; init; }
    public string ApiVersion              { get; init; } = "2024-06-20";
    public string ApiBase                 { get; init; } = "https://api.stripe.com/v1";
    public string? WebhookSecret          { get; init; }
}

public record RabbitMQConfiguration
{
    public required string AmqpUrl           { get; init; }
    public string QueueSapPrices             { get; init; } = "sap-price-changes";
    public string QueueIncidents             { get; init; } = "servicenow-incidents";
    public string QueueStripe                { get; init; } = "stripe-events";
    public string QueueApproval              { get; init; } = "approval-queue";
    public string QueueNotifications         { get; init; } = "notifications";
    public string QueueHubSpot               { get; init; } = "hubspot-sync";
    public string QueueDeadLetter            { get; init; } = "dead-letter-archive";
}
