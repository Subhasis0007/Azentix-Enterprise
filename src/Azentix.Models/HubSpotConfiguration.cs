namespace Azentix.Models;

public record HubSpotConfiguration
{
    public required string AccessToken { get; init; }
    public required string PortalId { get; init; }
    public string ApiBase { get; init; } = "https://api.hubapi.com";
}
