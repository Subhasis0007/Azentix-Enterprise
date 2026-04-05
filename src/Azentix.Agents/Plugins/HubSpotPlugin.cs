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
    private readonly ILogger<HubSpotPlugin> _logger;
    private readonly HubSpotConfiguration _cfg;

    public HubSpotPlugin(HttpClient http, ILogger<HubSpotPlugin> logger, HubSpotConfiguration cfg)
    {
        _http = http; _logger = logger; _cfg = cfg;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.AccessToken);
    }

    [KernelFunction("hubspot_get_contact")]
    [Description("Get a HubSpot contact by email or contact ID.")]
    public async Task<string> GetContactAsync(
        [Description("Email or contact ID")] string identifier,
        [Description("true if email address")] bool isEmail = true)
    {
        if (isEmail)
        {
            var payload = JsonSerializer.Serialize(new {
                filterGroups = new[] { new {
                    filters = new[] { new { propertyName = "email", @operator = "EQ", value = identifier } }
                }}
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_cfg.ApiBase}/crm/v3/objects/contacts/search", content);
            return await resp.Content.ReadAsStringAsync();
        }
        var r = await _http.GetAsync($"{_cfg.ApiBase}/crm/v3/objects/contacts/{identifier}?properties=email,firstname,lastname,company,lifecyclestage,hs_lead_status");
        return await r.Content.ReadAsStringAsync();
    }

    [KernelFunction("hubspot_create_contact")]
    [Description("Create a new HubSpot contact from a qualified Salesforce opportunity or lead.")]
    public async Task<string> CreateContactAsync(
        [Description("Email address")] string email,
        [Description("First name")] string firstName,
        [Description("Last name")] string lastName,
        [Description("Company name")] string company,
        [Description("Phone number")] string? phone = null,
        [Description("Lifecycle stage: lead, marketingqualifiedlead, salesqualifiedlead")] string stage = "lead")
    {
        var props = new Dictionary<string, string> {
            ["email"] = email, ["firstname"] = firstName, ["lastname"] = lastName,
            ["company"] = company, ["lifecyclestage"] = stage,
            ["hs_lead_status"] = "NEW", ["azentix_source"] = "Salesforce_Sync"
        };
        if (phone != null) props["phone"] = phone;
        var payload = JsonSerializer.Serialize(new { properties = props });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_cfg.ApiBase}/crm/v3/objects/contacts", content);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("hubspot_update_contact")]
    [Description("Update HubSpot contact properties. Used to flag failed Stripe payments.")]
    public async Task<string> UpdateContactAsync(
        [Description("HubSpot contact ID")] string contactId,
        [Description("JSON object of properties to update")] string propertiesJson)
    {
        var payload = JsonSerializer.Serialize(new { properties = JsonSerializer.Deserialize<object>(propertiesJson) });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PatchAsync($"{_cfg.ApiBase}/crm/v3/objects/contacts/{contactId}", content);
        return JsonSerializer.Serialize(new { success = resp.IsSuccessStatusCode, contactId });
    }

    [KernelFunction("hubspot_add_to_list")]
    [Description("Add a contact to a HubSpot static list for marketing segmentation.")]
    public async Task<string> AddToListAsync(
        [Description("HubSpot static list ID")] string listId,
        [Description("HubSpot contact ID")] string contactId)
    {
        var payload = JsonSerializer.Serialize(new { vids = new[] { long.Parse(contactId) } });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_cfg.ApiBase}/contacts/v1/lists/{listId}/add", content);
        return JsonSerializer.Serialize(new { success = resp.IsSuccessStatusCode, listId, contactId });
    }

    [KernelFunction("hubspot_get_deal")]
    [Description("Get a HubSpot deal by ID or search by company name.")]
    public async Task<string> GetDealAsync(
        [Description("Deal ID or company name")] string identifier,
        [Description("true to search by company name")] bool byCompany = false)
    {
        if (byCompany)
        {
            var payload = JsonSerializer.Serialize(new {
                filterGroups = new[] { new { filters = new[] { new {
                    propertyName = "dealname", @operator = "CONTAINS_TOKEN", value = identifier
                }}}}
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_cfg.ApiBase}/crm/v3/objects/deals/search", content);
            return await resp.Content.ReadAsStringAsync();
        }
        var r = await _http.GetAsync($"{_cfg.ApiBase}/crm/v3/objects/deals/{identifier}?properties=dealname,amount,dealstage");
        return await r.Content.ReadAsStringAsync();
    }
}
