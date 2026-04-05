namespace Azentix.Models;

public record RabbitMQConfiguration
{
    public required string AmqpUrl { get; init; }
    public string QueueSapPrices { get; init; } = "sap-price-changes";
    public string QueueIncidents { get; init; } = "servicenow-incidents";
    public string QueueStripe { get; init; } = "stripe-events";
    public string QueueApproval { get; init; } = "approval-queue";
    public string QueueNotifications { get; init; } = "notifications";
    public string QueueHubSpot { get; init; } = "hubspot-sync";
}
