namespace Azentix.Models;

public record SalesforceConfiguration
{
    public required string InstanceUrl { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public string ApiVersion { get; init; } = "v59.0";
}
