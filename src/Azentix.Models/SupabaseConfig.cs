namespace Azentix.Models;

public record SupabaseConfig
{
    public required string Url { get; init; }
    public required string AnonKey { get; init; }
    public required string ServiceKey { get; init; }
    public required string DatabaseConnectionString { get; init; }
}
