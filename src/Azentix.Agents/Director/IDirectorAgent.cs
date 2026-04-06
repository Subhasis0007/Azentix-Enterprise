using Azentix.Models;

namespace Azentix.Agents.Director;
public interface IDirectorAgent
{
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct = default);
}