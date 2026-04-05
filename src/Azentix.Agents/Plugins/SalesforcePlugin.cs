using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class SalesforcePlugin
{
    private readonly HttpClient _http;
    private readonly ILogger<SalesforcePlugin> _logger;
    private readonly SalesforceConfiguration _cfg;
    private string? _token;
    private string? _instanceUrl;

    public SalesforcePlugin(HttpClient http, ILogger<SalesforcePlugin> logger, SalesforceConfiguration cfg)
    { _http = http; _logger = logger; _cfg = cfg; }

    [KernelFunction("salesforce_get_product")]
    [Description("Get a Salesforce Product2 record by name or ID.")]
    public async Task<string> GetProductAsync(
        [Description("Product name or Salesforce ID")] string identifier,
        [Description("true if Salesforce ID")] bool isId = false)
    {
        await AuthAsync();
        var q = "SELECT Id,Name,ProductCode,IsActive FROM Product2 WHERE Name LIKE '%" + identifier + "%' LIMIT 5";
        var url = isId
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Product2/{identifier}?fields=Id,Name,ProductCode,IsActive"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString(q)}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("salesforce_get_pricebook")]
    [Description("Get the current Standard Pricebook entry for a Salesforce product. Returns UnitPrice and currency.")]
    public async Task<string> GetPricebookAsync(
        [Description("Salesforce Product2 ID")] string productId)
    {
        await AuthAsync();
        var q = "SELECT Id,UnitPrice,CurrencyIsoCode,IsActive FROM PricebookEntry " +
                "WHERE Product2Id='" + productId + "' AND Pricebook2.IsStandard=true AND IsActive=true LIMIT 1";
        var resp = await _http.GetAsync($"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString(q)}");
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("salesforce_update_price")]
    [Description("Update a Salesforce pricebook entry UnitPrice. Used when syncing from SAP.")]
    public async Task<string> UpdatePriceAsync(
        [Description("PricebookEntry ID")] string pricebookEntryId,
        [Description("New unit price")] decimal newPrice,
        [Description("Currency")] string currency = "GBP",
        [Description("SAP material number for audit")] string? sapMaterial = null)
    {
        await AuthAsync();
        var payload = JsonSerializer.Serialize(new {
            UnitPrice = newPrice,
            Azentix_SAP_Sync__c = true,
            Azentix_Last_Synced__c = DateTime.UtcNow.ToString("o"),
            Azentix_SAP_Material__c = sapMaterial ?? ""
        });
        var req = new HttpRequestMessage(new HttpMethod("PATCH"),
            $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/PricebookEntry/{pricebookEntryId}");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode, pricebookEntryId,
            newPrice, currency, sapMaterial, syncedAt = DateTime.UtcNow });
    }

    [KernelFunction("salesforce_get_lead")]
    [Description("Get a Salesforce Lead by ID or email address.")]
    public async Task<string> GetLeadAsync(
        [Description("Lead ID or email")] string identifier,
        [Description("true if email")] bool isEmail = false)
    {
        await AuthAsync();
        var url = isEmail
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString("SELECT Id,FirstName,LastName,Email,Company,Status FROM Lead WHERE Email='" + identifier + "' LIMIT 1")}"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Lead/{identifier}?fields=Id,FirstName,LastName,Email,Company,Status";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("salesforce_update_lead")]
    [Description("Update a Salesforce Lead with enriched data from SAP or other sources.")]
    public async Task<string> UpdateLeadAsync(
        [Description("Lead ID")] string leadId,
        [Description("JSON object of fields to update")] string fieldsJson)
    {
        await AuthAsync();
        var req = new HttpRequestMessage(new HttpMethod("PATCH"),
            $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Lead/{leadId}");
        req.Content = new StringContent(fieldsJson, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return JsonSerializer.Serialize(new { success = resp.IsSuccessStatusCode, leadId, updatedAt = DateTime.UtcNow });
    }

    [KernelFunction("salesforce_get_opportunity")]
    [Description("Get a Salesforce Opportunity by ID or by account name search.")]
    public async Task<string> GetOpportunityAsync(
        [Description("Opportunity ID or account name")] string identifier,
        [Description("true to search by account name")] bool byAccount = false)
    {
        await AuthAsync();
        var url = byAccount
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString("SELECT Id,Name,StageName,Amount,CloseDate FROM Opportunity WHERE Account.Name LIKE '%" + identifier + "%' LIMIT 5")}"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Opportunity/{identifier}?fields=Id,Name,StageName,Amount,CloseDate";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task AuthAsync()
    {
        if (_token != null) return;
        var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "password",
            ["client_id"] = _cfg.ClientId,
            ["client_secret"] = _cfg.ClientSecret,
            ["username"] = _cfg.Username,
            ["password"] = _cfg.Password
        });
        var resp = await _http.PostAsync("https://login.salesforce.com/services/oauth2/token", form);
        var data = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        _token = data.GetProperty("access_token").GetString();
        _instanceUrl = data.TryGetProperty("instance_url", out var iu) ? iu.GetString() : _cfg.InstanceUrl;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
    }
}
