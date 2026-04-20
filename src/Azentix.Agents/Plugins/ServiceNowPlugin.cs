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
    private readonly ServiceNowConfiguration _cfg;
    private readonly ILogger<ServiceNowPlugin> _log;

    public ServiceNowPlugin(HttpClient http, ServiceNowConfiguration cfg,
        ILogger<ServiceNowPlugin> log)
    {
        _http = http; _cfg = cfg; _log = log;
        var creds = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.Password}"));
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    [KernelFunction("servicenow_get_incident")]
    [Description("Get a ServiceNow incident by number (INC0001234) or sys_id.")]
    public async Task<string> GetIncidentAsync(
        [Description("Incident number or sys_id")] string id,
        [Description("true if incident number format")] bool isNumber = true)
    {
        var fields = "sys_id,number,short_description,description,state,priority,category,assignment_group,assigned_to";
        var url = isNumber
            ? $"{_cfg.InstanceUrl}/api/now/table/incident?sysparm_query=number={id}&sysparm_fields={fields}&sysparm_limit=1"
            : $"{_cfg.InstanceUrl}/api/now/table/incident/{id}?sysparm_fields={fields}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("servicenow_update_incident")]
    [Description("Update a ServiceNow incident state, priority, assignment, or add work note.")]
    public async Task<string> UpdateIncidentAsync(
        [Description("Incident sys_id")] string sysId,
        [Description("State: 1=New 2=InProgress 6=Resolved 7=Closed")] string? state = null,
        [Description("Assignment group name")] string? assignmentGroup = null,
        [Description("Priority: 1=Critical 2=High 3=Medium 4=Low")] string? priority = null,
        [Description("Work note text to append")] string? workNote = null,
        [Description("Category value")] string? category = null)
    {
        var body = new Dictionary<string, object>();
        if (state != null) body["state"] = state;
        if (priority != null) body["priority"] = priority;
        if (category != null) body["category"] = category;
        if (workNote != null) body["work_notes"] = $"[Azentix Agent] {workNote}";

        string? assignmentGroupSysId = null;
        if (!string.IsNullOrWhiteSpace(assignmentGroup))
        {
            assignmentGroupSysId = await ResolveAssignmentGroupSysIdAsync(assignmentGroup);
            if (!string.IsNullOrWhiteSpace(assignmentGroupSysId))
            {
                body["assignment_group"] = assignmentGroupSysId;
            }
            else
            {
                _log.LogWarning(
                    "ServiceNow assignment group not found by name. Sending original value as fallback. Group={Group}",
                    assignmentGroup);
                body["assignment_group"] = assignmentGroup;
            }
        }

        var content = new StringContent(JsonSerializer.Serialize(body),
            Encoding.UTF8, "application/json");
        var resp = await _http.PatchAsync(
            $"{_cfg.InstanceUrl}/api/now/table/incident/{sysId}", content);
        var responseBody = await resp.Content.ReadAsStringAsync();

        var verification = await VerifyIncidentAsync(sysId);

        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode, sysId,
            statusCode = (int)resp.StatusCode,
            assignmentGroupRequested = assignmentGroup,
            assignmentGroupResolvedSysId = assignmentGroupSysId,
            updatedFields = body.Keys.ToArray(),
            details = ParseJsonOrRaw(responseBody),
            verification
        });
    }

    [KernelFunction("servicenow_create_incident")]
    [Description("Create a new ServiceNow incident (e.g. for a Stripe payment failure).")]
    public async Task<string> CreateIncidentAsync(
        [Description("Short description (max 160 chars)")] string shortDescription,
        [Description("Full description")] string description,
        [Description("Category e.g. sap, stripe, network")] string category,
        [Description("Urgency 1=High 2=Medium 3=Low")] string urgency = "2",
        [Description("Impact 1=High 2=Medium 3=Low")] string impact = "2",
        [Description("Assignment group name")] string? assignmentGroup = null)
    {
        var body = new Dictionary<string, string> {
            ["short_description"] = shortDescription,
            ["description"]       = $"[Azentix Auto-Created]\n{description}",
            ["category"]          = category,
            ["urgency"]           = urgency,
            ["impact"]            = impact,
            ["caller_id"]         = "azentix_agent"
        };
        if (assignmentGroup != null) body["assignment_group"] = assignmentGroup;
        var content = new StringContent(JsonSerializer.Serialize(body),
            Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(
            $"{_cfg.InstanceUrl}/api/now/table/incident", content);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("servicenow_search_knowledge")]
    [Description("Search the ServiceNow knowledge base for matching articles.")]
    public async Task<string> SearchKnowledgeAsync(
        [Description("Search query text")] string query,
        [Description("Maximum results to return")] int limit = 5)
    {
        var q = Uri.EscapeDataString(query[..Math.Min(50, query.Length)]);
        var url = $"{_cfg.InstanceUrl}/api/now/table/kb_knowledge" +
                  $"?sysparm_query=textLIKE{q}^workflow_state=published" +
                  $"&sysparm_fields=short_description,text,kb_category&sysparm_limit={limit}";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string?> ResolveAssignmentGroupSysIdAsync(string assignmentGroup)
    {
        var escaped = Uri.EscapeDataString(assignmentGroup.Trim());
        var url = $"{_cfg.InstanceUrl}/api/now/table/sys_user_group" +
                  $"?sysparm_query=name={escaped}" +
                  "&sysparm_fields=sys_id,name" +
                  "&sysparm_limit=1";

        var resp = await _http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return null;

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (!json.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array ||
                result.GetArrayLength() == 0)
                return null;

            var first = result[0];
            return first.TryGetProperty("sys_id", out var sysId)
                ? sysId.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<object> VerifyIncidentAsync(string sysId)
    {
        var fields = "sys_id,number,state,priority,category,assignment_group,sys_updated_on";
        var url = $"{_cfg.InstanceUrl}/api/now/table/incident/{sysId}?sysparm_fields={fields}&sysparm_display_value=all";
        var resp = await _http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();

        return new
        {
            success = resp.IsSuccessStatusCode,
            statusCode = (int)resp.StatusCode,
            incident = ParseJsonOrRaw(body)
        };
    }

    private static object ParseJsonOrRaw(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch
        {
            return value;
        }
    }
}
