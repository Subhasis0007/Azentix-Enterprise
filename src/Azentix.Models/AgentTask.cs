namespace Azentix.Models;

/// <summary>AgentTask — the input to every Azentix agent execution.</summary>
public record AgentTask
{
    public string TaskId { get; init; } = Guid.NewGuid().ToString();
    public required string TaskType { get; init; }
    public required string Description { get; init; }
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public Dictionary<string, object> InputData { get; init; } = new();
    public string Context { get; init; } = string.Empty;
    public string ExpectedOutputFormat { get; init; } = "JSON";
    public string? CorrelationId { get; init; }
    public string? RequestedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum TaskPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

/// <summary>AgentResult — everything the agent produces. Sent back to n8n and logged to Grafana.</summary>
public record AgentResult
{
    public required string TaskId { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Pending;
    public string? FinalAnswer { get; set; }
    public string? ErrorMessage { get; set; }
    public List<AuditEntry> AuditTrail { get; set; } = new();
    public int TotalIterations { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt - StartedAt;
    public Dictionary<string, object> OutputData { get; set; } = new();
    public int TotalTokensUsed { get; set; }
}

public enum AgentStatus { Pending, Running, Completed, Failed, MaxIterationsReached, HumanReviewRequired }

/// <summary>AuditEntry — one Thought/Action/Observation cycle. Logged to Grafana Loki.</summary>
public record AuditEntry
{
    public int Iteration { get; init; }
    public DateTime Timestamp { get; init; }
    public string AgentThought { get; init; } = string.Empty;
    public string AgentAction { get; init; } = string.Empty;
    public string? ActionResult { get; init; }
    public string TokensUsed { get; init; } = "0";
}

/// <summary>AgentConfiguration — tuning params. Loaded from Doppler secrets.</summary>
public record AgentConfiguration
{
    public int MaxIterations { get; init; } = 10;
    public int TimeoutSeconds { get; init; } = 60;
    public int MaxTokensPerIteration { get; init; } = 2000;
    public int TokenBudget { get; init; } = 16000;
    public string ModelDeployment { get; init; } = "gpt-4o-mini";
}
