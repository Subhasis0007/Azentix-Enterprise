using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class HubSpotPlugin
{
    private readonly HttpClient _http;
    private readonly HubSpotConfiguration _cfg;
    private readonly ILogger<HubSpotPlugin> _log;

    public HubSpotPlugin(HttpClient http, HubSpotConfiguration cfg,
        ILogger<HubSpotPlugin> log)
    {
        _http = http; _cfg = cfg; _log = log;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.AccessToken);
    }

    [KernelFunction("hubspot_get_contact")]
    [Description("Get a HubSpot contact by email or contact ID.")]
    public async Task<string> GetContactAsync(
        [Description("Email or HubSpot contact ID")] string identifier,
        [Description("true if email address")] bool isEmail = true)
    {
        if (isEmail)
        {
            var body = JsonSerializer.Serialize(new {
                filterGroups = new[] { new {
                    filters = new[] { new {
                        propertyName = "email", @operator = "EQ", value = identifier
                    }}
                }}
            });
            var resp = await _http.PostAsync(
                $"{_cfg.ApiBase}/crm/v3/objects/contacts/search",
                new StringContent(body, Encoding.UTF8, "application/json"));
            return await resp.Content.ReadAsStringAsync();
        }
        var r = await _http.GetAsync(
            $"{_cfg.ApiBase}/crm/v3/objects/contacts/{identifier}" +
            "?properties=email,firstname,lastname,company,lifecyclestage");
        return await r.Content.ReadAsStringAsync();
    }

    [KernelFunction("hubspot_create_contact")]
    [Description("Create a new HubSpot contact from a qualified Salesforce lead or opportunity.")]
    public async Task<string> CreateContactAsync(
        [Description("Email address")] string email,
        [Description("First name")] string firstName,
        [Description("Last name")] string lastName,
        [Description("Company name")] string company,
        [Description("Phone number (optional)")] string? phone = null,
        [Description("Lifecycle stage")] string stage = "salesqualifiedlead")
    {
        var props = new Dictionary<string, string> {
            ["email"]          = email,
            ["firstname"]      = firstName,
            ["lastname"]       = lastName,
            ["company"]        = company,
            ["lifecyclestage"] = stage,
            ["hs_lead_status"] = "NEW",
            ["azentix_source"] = "Salesforce_Sync"
        };
        if (phone is not null) props["phone"] = phone;
        var body = JsonSerializer.Serialize(new { properties = props });
        var resp = await _http.PostAsync(
            $"{_cfg.ApiBase}/crm/v3/objects/contacts",
            new StringContent(body, Encoding.UTF8, "application/json"));
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("hubspot_update_contact")]
    [Description("Update HubSpot contact properties.")]
    public async Task<string> UpdateContactAsync(
        [Description("HubSpot contact ID")] string contactId,
        [Description("JSON object of properties to update")] string propertiesJson)
    {
        var body = JsonSerializer.Serialize(new {
            properties = JsonSerializer.Deserialize<object>(propertiesJson) });
        var resp = await _http.PatchAsync(
            $"{_cfg.ApiBase}/crm/v3/objects/contacts/{contactId}",
            new StringContent(body, Encoding.UTF8, "application/json"));
        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode, contactId });
    }

    [KernelFunction("hubspot_add_to_list")]
    [Description("Add a contact to a HubSpot static list.")]
    public async Task<string> AddToListAsync(
        [Description("HubSpot list ID")] string listId,
        [Description("HubSpot contact ID")] string contactId)
    {
        var body = JsonSerializer.Serialize(new { vids = new[] { long.Parse(contactId) } });
        var resp = await _http.PostAsync(
            $"{_cfg.ApiBase}/contacts/v1/lists/{listId}/add",
            new StringContent(body, Encoding.UTF8, "application/json"));
        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode, listId, contactId });
    }

    [KernelFunction("hubspot_get_deal")]
    [Description("Get a HubSpot deal by ID or company name.")]
    public async Task<string> GetDealAsync(
        [Description("Deal ID or company name")] string identifier,
        [Description("true to search by company name")] bool byCompany = false)
    {
        if (byCompany)
        {
            var body = JsonSerializer.Serialize(new {
                filterGroups = new[] { new {
                    filters = new[] { new {
                        propertyName = "dealname", @operator = "CONTAINS_TOKEN", value = identifier
                    }}
                }}
            });
            var resp = await _http.PostAsync(
                $"{_cfg.ApiBase}/crm/v3/objects/deals/search",
                new StringContent(body, Encoding.UTF8, "application/json"));
            return await resp.Content.ReadAsStringAsync();
        }
        var r = await _http.GetAsync(
            $"{_cfg.ApiBase}/crm/v3/objects/deals/{identifier}" +
            "?properties=dealname,amount,dealstage");
        return await r.Content.ReadAsStringAsync();
    }
}
