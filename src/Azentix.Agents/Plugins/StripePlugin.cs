using Microsoft.SemanticKernel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class StripePlugin
{
    private readonly HttpClient _http;
    private readonly StripeConfiguration _cfg;
    private readonly ILogger<StripePlugin> _log;

    public StripePlugin(HttpClient http, StripeConfiguration cfg,
        ILogger<StripePlugin> log)
    {
        _http = http; _cfg = cfg; _log = log;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.SecretKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Stripe-Version", cfg.ApiVersion);
    }

    [KernelFunction("stripe_get_payment")]
    [Description("Get a Stripe payment intent or charge by ID.")]
    public async Task<string> GetPaymentAsync(
        [Description("Payment intent ID (pi_...) or charge ID (ch_...)")] string paymentId)
    {
        var endpoint = paymentId.StartsWith("pi_") ? "payment_intents" : "charges";
        var resp = await _http.GetAsync($"{_cfg.ApiBase}/{endpoint}/{paymentId}");
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_get_customer")]
    [Description("Get a Stripe customer by ID or email.")]
    public async Task<string> GetCustomerAsync(
        [Description("Customer ID (cus_...) or email address")] string identifier,
        [Description("true if email address")] bool isEmail = false)
    {
        var url = isEmail
            ? $"{_cfg.ApiBase}/customers?email={Uri.EscapeDataString(identifier)}&limit=1"
            : $"{_cfg.ApiBase}/customers/{identifier}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_list_failed_payments")]
    [Description("List recent failed Stripe payment intents.")]
    public async Task<string> ListFailedPaymentsAsync(
        [Description("Maximum number of results")] int limit = 10,
        [Description("Only return results after this Unix timestamp")] long? createdAfter = null)
    {
        var url = $"{_cfg.ApiBase}/payment_intents?status=requires_payment_method&limit={limit}";
        if (createdAfter.HasValue) url += $"&created[gte]={createdAfter.Value}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("stripe_get_subscription")]
    [Description("Get a Stripe subscription by ID to check impact of a failed payment.")]
    public async Task<string> GetSubscriptionAsync(
        [Description("Subscription ID (sub_...)")] string subscriptionId)
    {
        var resp = await _http.GetAsync($"{_cfg.ApiBase}/subscriptions/{subscriptionId}");
        return await resp.Content.ReadAsStringAsync();
    }
}
