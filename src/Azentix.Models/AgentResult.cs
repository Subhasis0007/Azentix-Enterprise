
namespace Azentix.Models;

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
