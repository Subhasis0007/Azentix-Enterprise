namespace Azentix.Models;

/// <summary>AgentConfiguration — tuning params. Loaded from Doppler secrets.</summary>
public record AgentConfiguration
{
    public int MaxIterations { get; init; } = 10;
    public int TimeoutSeconds { get; init; } = 60;
    public int MaxTokensPerIteration { get; init; } = 2000;
    public int TokenBudget { get; init; } = 16000;
    public string ModelProvider { get; init; } = "azure";
    public string ModelDeployment { get; init; } = "gpt-4o-mini";
}
