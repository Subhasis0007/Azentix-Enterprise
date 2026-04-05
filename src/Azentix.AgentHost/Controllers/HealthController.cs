using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azentix.AgentHost.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _health;

    public HealthController(HealthCheckService health) { _health = health; }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var report = await _health.CheckHealthAsync();
        var body   = new {
            status  = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(),
                           description = e.Value.Description })
        };
        return report.Status == HealthStatus.Healthy ? Ok(body) : StatusCode(503, body);
    }
}
