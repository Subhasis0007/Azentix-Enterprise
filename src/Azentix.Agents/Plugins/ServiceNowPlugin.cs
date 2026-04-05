using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class ServiceNowPlugin
{
    private readonly HttpClient _http;
    private readonly ILogger<ServiceNowPlugin> _logger;
    private readonly ServiceNowConfiguration _cfg;

    public ServiceNowPlugin(HttpClient http, ILogger<ServiceNowPlugin> logger, ServiceNowConfiguration cfg)
    {
        _http = http; _logger = logger; _cfg = cfg;
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes(cfg.Username + ":" + cfg.Password));
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    [KernelFunction("servicenow_get_incident")]
    [Description("Get a ServiceNow incident by number (e.g. INC0001234) or sys_id.")]
    public async Task<string> GetIncidentAsync(
        [Description("Incident number or sys_id")] string id,
        [Description("true if incident number like INC0001234")] bool isNumber = true)
    {
        var url = isNumber
            ? $"{_cfg.InstanceUrl}/api/now/table/incident?sysparm_query=number={id}&sysparm_fields=sys_id,number,short_description,description,state,priority,category,assignment_group,assigned_to&sysparm_limit=1"
            : $"{_cfg.InstanceUrl}/api/now/table/incident/{id}?sysparm_fields=sys_id,number,short_description,description,state,priority,category,assignment_group,assigned_to";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("servicenow_update_incident")]
    [Description("Update a ServiceNow incident state, priority, assignment group, or add work note.")]
    public async Task<string> UpdateIncidentAsync(
        [Description("Incident sys_id")] string sysId,
        [Description("State: 1=New 2=InProgress 6=Resolved 7=Closed")] string? state = null,
        [Description("Assignment group name")] string? assignmentGroup = null,
        [Description("Priority: 1=Critical 2=High 3=Medium 4=Low")] string? priority = null,
        [Description("Work note to add")] string? workNote = null,
        [Description("Category")] string? category = null)
    {
        var payload = new Dictionary<string, string>();
        if (state != null) payload["state"] = state;
        if (assignmentGroup != null) payload["assignment_group"] = assignmentGroup;
        if (priority != null) payload["priority"] = priority;
        if (category != null) payload["category"] = category;
        if (workNote != null) payload["work_notes"] = "[Azentix Agent] " + workNote;
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PatchAsync($"{_cfg.InstanceUrl}/api/now/table/incident/{sysId}", content);
        return JsonSerializer.Serialize(new { success = resp.IsSuccessStatusCode, sysId, updated = payload.Keys });
    }

    [KernelFunction("servicenow_create_incident")]
    [Description("Create a new ServiceNow incident. Used when Stripe or monitoring detects an issue.")]
    public async Task<string> CreateIncidentAsync(
        [Description("Short description")] string shortDescription,
        [Description("Detailed description")] string description,
        [Description("Category e.g. sap, salesforce, stripe, network")] string category,
        [Description("Urgency 1=High 2=Medium 3=Low")] string urgency = "2",
        [Description("Impact 1=High 2=Medium 3=Low")] string impact = "2",
        [Description("Assignment group")] string? assignmentGroup = null)
    {
        var payload = new Dictionary<string, string> {
            ["short_description"] = shortDescription,
            ["description"] = "[Azentix Auto-Created]\n" + description,
            ["category"] = category,
            ["urgency"] = urgency,
            ["impact"] = impact,
            ["caller_id"] = "azentix_agent"
        };
        if (assignmentGroup != null) payload["assignment_group"] = assignmentGroup;
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_cfg.InstanceUrl}/api/now/table/incident", content);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("servicenow_search_knowledge")]
    [Description("Search ServiceNow knowledge base for articles matching an incident or error.")]
    public async Task<string> SearchKnowledgeAsync(
        [Description("Search query")] string query,
        [Description("Max articles")] int limit = 5)
    {
        var url = $"{_cfg.InstanceUrl}/api/now/table/kb_knowledge?" +
                  "sysparm_query=textLIKE" + Uri.EscapeDataString(query[..Math.Min(50, query.Length)]) +
                  "^workflow_state=published&sysparm_fields=short_description,text,kb_category&sysparm_limit=" + limit;
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }
}
