using Microsoft.AspNetCore.Mvc;
using Azentix.Agents.Director;
using Azentix.Models;

namespace Azentix.AgentHost.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IDirectorAgent _director;
    private readonly ILogger<WebhookController> _log;
    private readonly IConfiguration _cfg;

    public WebhookController(IDirectorAgent director,
        ILogger<WebhookController> log, IConfiguration cfg)
    { _director = director; _log = log; _cfg = cfg; }

    /// <summary>Receives Stripe webhook events. Fires billing-alert agent async.</summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        _log.LogInformation("Stripe webhook received ({Len} bytes)", json.Length);

        // Signature validation
        var secret = _cfg["STRIPE_WEBHOOK_SECRET"];
        if (!string.IsNullOrEmpty(secret))
        {
            try
            {
                Stripe.EventUtility.ConstructEvent(
                    json, Request.Headers["Stripe-Signature"], secret);
            }
            catch
            {
                return BadRequest(new { error = "Invalid Stripe signature" });
            }
        }

        var evt  = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        var type = evt.TryGetProperty("type", out var t) ? t.GetString() : "unknown";

        if (type is "payment_intent.payment_failed" or "customer.subscription.deleted")
        {
            _ = Task.Run(() => _director.ExecuteAsync(new AgentTask {
                TaskType    = "stripe-billing-alert",
                Description = $"Stripe event {type} received. Investigate and trigger incident.",
                Priority    = TaskPriority.High,
                InputData   = new Dictionary<string, object> {
                    ["stripeEventType"] = type ?? "",
                    ["stripeEventId"]   = evt.TryGetProperty("id", out var id)
                        ? id.GetString() ?? "" : ""
                },
                Context = "Stripe webhook. Create ServiceNow incident, flag Salesforce, update HubSpot."
            }));
        }
        return Ok(new { received = true, type });
    }
}
