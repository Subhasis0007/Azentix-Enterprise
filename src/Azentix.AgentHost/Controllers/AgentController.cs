using Microsoft.AspNetCore.Mvc;
using Azentix.Agents.Director;
using Azentix.Models;

namespace Azentix.AgentHost.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentController : ControllerBase
{
    private readonly IDirectorAgent _director;
    private readonly ILogger<AgentController> _logger;

    public AgentController(IDirectorAgent director, ILogger<AgentController> logger)
    { _director = director; _logger = logger; }

    /// <summary>Execute an agent task. Called by n8n workflows via Kong Gateway.</summary>
    [HttpPost("execute")]
    [ProducesResponseType(typeof(AgentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Execute(
        [FromBody] AgentTask task,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.TaskType))
            return BadRequest(new { error = "TaskType is required" });

        _logger.LogInformation("POST /api/agents/execute | TaskId={Id} | Type={T}", task.TaskId, task.TaskType);
        var result = await _director.ExecuteAsync(task, ct);
        return Ok(result);
    }

    /// <summary>Get agent status. Used by n8n to poll for async task completion.</summary>
    [HttpGet("status")]
    public IActionResult Status() => Ok(new {
        status = "ready",
        timestamp = DateTime.UtcNow,
        version = "1.0.0"
    });
}
