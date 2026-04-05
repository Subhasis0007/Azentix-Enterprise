namespace Azentix.Models;

public record SapConfiguration
{
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public string System { get; init; } = "SANDBOX";
    public string DefaultSalesOrg { get; init; } = "GB01";
}
