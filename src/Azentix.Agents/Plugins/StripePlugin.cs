using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class StripePlugin
{
    private readonly HttpClient _http;
    private readonly ILogger<StripePlugin> _logger;
    private readonly StripeConfiguration _cfg;

    public StripePlugin(HttpClient http, ILogger<StripePlugin> logger, StripeConfiguration cfg)
    {
        _http = http; _logger = logger; _cfg = cfg;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.SecretKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Stripe-Version", cfg.ApiVersion);
    }

    [KernelFunction("stripe_get_payment")]
    [Description("Get details of a Stripe payment intent or charge by ID.")]
    public async Task<string> GetPaymentAsync(
        [Description("Payment intent ID (pi_...) or charge ID (ch_...)")] string paymentId)
    {
        var endpoint = paymentId.StartsWith("pi_") ? "payment_intents" : "charges";
        var resp = await _http.GetAsync($"{_cfg.ApiBase}/{endpoint}/{paymentId}");
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_get_customer")]
    [Description("Get a Stripe customer by ID or email address.")]
    public async Task<string> GetCustomerAsync(
        [Description("Customer ID (cus_...) or email")] string identifier,
        [Description("true if email")] bool isEmail = false)
    {
        var url = isEmail
            ? $"{_cfg.ApiBase}/customers?email={Uri.EscapeDataString(identifier)}&limit=1"
            : $"{_cfg.ApiBase}/customers/{identifier}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_list_failed_payments")]
    [Description("List recent failed Stripe payment intents. Used to trigger incident creation.")]
    public async Task<string> ListFailedPaymentsAsync(
        [Description("Maximum results")] int limit = 10,
        [Description("Unix timestamp — only after this time")] long? createdAfter = null)
    {
        var url = $"{_cfg.ApiBase}/payment_intents?status=requires_payment_method&limit={limit}";
        if (createdAfter.HasValue) url += "&created[gte]=" + createdAfter.Value;
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_get_subscription")]
    [Description("Get a Stripe subscription by ID. Checks impact of failed payment.")]
    public async Task<string> GetSubscriptionAsync(
        [Description("Subscription ID (sub_...)")] string subscriptionId)
    {
        var resp = await _http.GetAsync($"{_cfg.ApiBase}/subscriptions/{subscriptionId}");
        return await resp.Content.ReadAsStringAsync();
    }
}
