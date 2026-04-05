using Microsoft.AspNetCore.Mvc;
using Azentix.Agents.Director;
using Azentix.Models;
using Stripe;

namespace Azentix.AgentHost.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IDirectorAgent _director;
    private readonly ILogger<WebhookController> _logger;
    private readonly IConfiguration _config;

    public WebhookController(IDirectorAgent director, ILogger<WebhookController> logger, IConfiguration config)
    { _director = director; _logger = logger; _config = config; }

    /// <summary>Receives Stripe webhook events. Triggers agent for failed payments.</summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var webhookSecret = _config["STRIPE_WEBHOOK_SECRET"];

        Event stripeEvent;
        try
        {
            stripeEvent = webhookSecret != null
                ? EventUtility.ConstructEvent(json,
                    Request.Headers["Stripe-Signature"], webhookSecret)
                : EventUtility.ParseEvent(json);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature validation failed: {Msg}", ex.Message);
            return BadRequest(new { error = "Invalid signature" });
        }

        _logger.LogInformation("Stripe webhook: {Type}", stripeEvent.Type);

        if (stripeEvent.Type == "payment_intent.payment_failed" ||
            stripeEvent.Type == "customer.subscription.deleted")
        {
            var task = new AgentTask {
                TaskType    = "stripe-billing-alert",
                Description = $"Stripe event: {stripeEvent.Type}. Investigate and trigger incident + CRM update.",
                Priority    = TaskPriority.High,
                InputData   = new Dictionary<string, object> {
                    ["stripeEventType"] = stripeEvent.Type,
                    ["stripeEventId"]   = stripeEvent.Id,
                    ["stripeObjectId"]  = stripeEvent.Data?.Object?.ToString() ?? ""
                },
                Context = "Stripe webhook. Check customer, create ServiceNow incident, flag Salesforce opportunity, update HubSpot."
            };
            _ = Task.Run(() => _director.ExecuteAsync(task));
        }

        return Ok(new { received = true });
    }
}
