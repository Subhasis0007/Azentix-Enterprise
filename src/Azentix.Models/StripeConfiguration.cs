namespace Azentix.Models;

public record StripeConfiguration
{
    public required string SecretKey { get; init; }
    public string ApiVersion { get; init; } = "2024-06-20";
    public string ApiBase { get; init; } = "https://api.stripe.com/v1";
    public string? WebhookSecret { get; init; }
}
