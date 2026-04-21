using Microsoft.AspNetCore.Mvc;
using Azentix.Agents.Director;
using Azentix.Models;

namespace Azentix.AgentHost.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentController : ControllerBase
{
    private readonly IDirectorAgent _director;
    private readonly ILogger<AgentController> _log;

    public AgentController(IDirectorAgent director, ILogger<AgentController> log)
    { _director = director; _log = log; }

    /// <summary>Execute an agent task via chat UI, API clients, or automation through Kong.</summary>
    [HttpPost("execute")]
    [ProducesResponseType(typeof(AgentResult), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Execute([FromBody] AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.TaskType))
            return BadRequest(new { error = "TaskType is required" });

        _log.LogInformation("POST execute | {Id} | {Type}", task.TaskId, task.TaskType);
        var result = await _director.ExecuteAsync(task, ct);
        return Ok(result);
    }

    /// <summary>Agent readiness check.</summary>
    [HttpGet("status")]
    public IActionResult Status() =>
        Ok(new { status = "ready", timestamp = DateTime.UtcNow, version = "1.0.0" });
}
