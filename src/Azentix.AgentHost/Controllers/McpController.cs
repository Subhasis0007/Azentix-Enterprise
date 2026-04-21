using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Azentix.Agents.Director;
using Azentix.Models;

namespace Azentix.AgentHost.Controllers;

/// <summary>
/// MCP (Model Context Protocol) server endpoint.
///
/// Implements the JSON-RPC 2.0 MCP specification so any MCP-compatible
/// client (Claude Desktop, Cursor, VS Code Copilot) can discover and
/// call Azentix tools natively.
///
/// Endpoints:
///   POST /mcp          — JSON-RPC 2.0 dispatcher (initialize, tools/list, tools/call)
///   GET  /mcp/tools    — REST shortcut: list all available tools
///   GET  /mcp/stream   — SSE stream: server-sent events for async task results
/// </summary>
[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private readonly Kernel          _kernel;
    private readonly IDirectorAgent  _director;
    private readonly ILogger<McpController> _log;

    private const string McpVersion    = "2024-11-05";
    private const string ServerName    = "azentix-enterprise";
    private const string ServerVersion = "1.0.0";

    public McpController(Kernel kernel, IDirectorAgent director,
        ILogger<McpController> log)
    {
        _kernel   = kernel;
        _director = director;
        _log      = log;
    }

    // ────────────────────────────────────────────────────────────────────
    // POST /mcp  — MCP JSON-RPC 2.0 dispatcher
    // ────────────────────────────────────────────────────────────────────
    [HttpPost]
    [Produces("application/json")]
    public async Task<IActionResult> Dispatch(
        [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!body.TryGetProperty("method", out var methodEl))
            return BadRequest(McpError(-32600, "Invalid Request: missing method"));

        var method = methodEl.GetString() ?? "";
        var id     = body.TryGetProperty("id", out var idEl) ? idEl : default;

        _log.LogDebug("MCP method: {Method}", method);

        return method switch
        {
            "initialize"   => Ok(HandleInitialize(id)),
            "tools/list"   => Ok(HandleToolsList(id)),
            "tools/call"   => Ok(await HandleToolsCall(body, id, ct)),
            "ping"         => Ok(McpResult(id, new { pong = true })),
            _              => Ok(McpError(-32601, $"Method not found: {method}", id))
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // GET /mcp/tools  — REST shortcut for tool discovery
    // ────────────────────────────────────────────────────────────────────
    [HttpGet("tools")]
    [Produces("application/json")]
    public IActionResult GetTools() =>
        Ok(new { tools = BuildToolList() });

    // ────────────────────────────────────────────────────────────────────
    // GET /mcp/stream  — SSE stream for async agent result events
    // ────────────────────────────────────────────────────────────────────
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string taskType = "general",
        [FromQuery] string description = "Status check",
        CancellationToken ct = default)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control",  "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        async Task WriteEvent(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            var msg  = $"event: {eventName}\ndata: {json}\n\n";
            await Response.WriteAsync(msg, Encoding.UTF8, ct);
            await Response.Body.FlushAsync(ct);
        }

        await WriteEvent("connected", new { message = "MCP SSE stream connected", ts = DateTime.UtcNow });

        try
        {
            await WriteEvent("task_started", new { taskType, ts = DateTime.UtcNow });

            var result = await _director.ExecuteAsync(new AgentTask
            {
                TaskType    = taskType,
                Description = description,
                Priority    = TaskPriority.Normal,
                InputData   = new Dictionary<string, object>(),
                Context     = "MCP SSE stream request"
            }, ct);

            var isSuccess = result.Status == AgentStatus.Completed;
            var summary = result.FinalAnswer ?? result.ErrorMessage ?? "Task completed";
            var durationMs = result.Duration?.TotalMilliseconds;

            await WriteEvent("task_completed", new
            {
                success     = isSuccess,
                summary,
                taskId      = result.TaskId,
                durationMs,
                ts          = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            await WriteEvent("cancelled", new { ts = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MCP SSE stream error");
            await WriteEvent("error", new { message = ex.Message, ts = DateTime.UtcNow });
        }
        finally
        {
            await WriteEvent("done", new { ts = DateTime.UtcNow });
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────

    private object HandleInitialize(JsonElement id) => McpResult(id, new
    {
        protocolVersion = McpVersion,
        serverInfo = new { name = ServerName, version = ServerVersion },
        capabilities = new
        {
            tools = new { listChanged = false },
            resources = new { },
            prompts = new { }
        }
    });

    private object HandleToolsList(JsonElement id) =>
        McpResult(id, new { tools = BuildToolList() });

    private async Task<object> HandleToolsCall(
        JsonElement body, JsonElement id, CancellationToken ct)
    {
        if (!body.TryGetProperty("params", out var paramsEl))
            return McpError(-32602, "Invalid params: missing params", id);

        if (!paramsEl.TryGetProperty("name", out var nameEl))
            return McpError(-32602, "Invalid params: missing tool name", id);

        var toolName   = nameEl.GetString() ?? "";
        var arguments  = paramsEl.TryGetProperty("arguments", out var argsEl)
            ? argsEl : default;

        _log.LogInformation("MCP tools/call: {Tool}", toolName);

        // Map MCP tool call → DirectorAgent task
        var inputData = new Dictionary<string, object>();
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                inputData[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
        }

        var (taskType, description) = McpToolToAgentTask(toolName, inputData);

        try
        {
            var result = await _director.ExecuteAsync(new AgentTask
            {
                TaskType    = taskType,
                Description = description,
                Priority    = TaskPriority.High,
                InputData   = inputData,
                Context     = $"MCP tool call: {toolName}"
            }, ct);

            var isSuccess = result.Status == AgentStatus.Completed;
            var summary = result.FinalAnswer ?? result.ErrorMessage ?? "Task completed";

            return McpResult(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = summary
                    }
                },
                isError = !isSuccess
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MCP tool call failed: {Tool}", toolName);
            return McpError(-32000, $"Tool execution failed: {ex.Message}", id);
        }
    }

    private static (string taskType, string description) McpToolToAgentTask(
        string toolName, Dictionary<string, object> args)
    {
        return toolName switch
        {
            "sap_get_price"          => ("sap-price-sync",
                $"Fetch SAP price for material {args.GetValueOrDefault("material", "unknown")}"),
            "sap_sync_salesforce"    => ("sap-to-salesforce-price-sync",
                $"Sync SAP price for {args.GetValueOrDefault("material", "unknown")} to Salesforce product {args.GetValueOrDefault("sf_product_id", "unknown")}"),
            "servicenow_create_incident" => ("servicenow-incident",
                $"Create ServiceNow incident: {args.GetValueOrDefault("description", "No description")}"),
            "servicenow_triage"      => ("incident-triage",
                $"Triage incident: {args.GetValueOrDefault("incident_id", "unknown")}"),
            "hubspot_sync_contact"   => ("hubspot-contact-sync",
                $"Sync HubSpot contact {args.GetValueOrDefault("email", "unknown")}"),
            "stripe_check_billing"   => ("stripe-billing-check",
                $"Check Stripe billing for customer {args.GetValueOrDefault("customer_id", "unknown")}"),
            "agent_execute"          => (
                args.GetValueOrDefault("task_type", "general").ToString()!,
                args.GetValueOrDefault("description", "MCP agent execution").ToString()!),
            _                        => ("general",
                $"Execute tool {toolName} with args: {System.Text.Json.JsonSerializer.Serialize(args)}")
        };
    }

    private List<object> BuildToolList()
    {
        var tools = new List<object>
        {
            McpTool("sap_get_price",
                "Fetch the current price for a SAP material from S/4HANA",
                new { material = McpParam("string", "SAP material number", true) }),

            McpTool("sap_sync_salesforce",
                "Sync a SAP material price into a Salesforce pricebook entry",
                new {
                    material      = McpParam("string", "SAP material number", true),
                    sf_product_id = McpParam("string", "Salesforce Product2 ID", true)
                }),

            McpTool("servicenow_create_incident",
                "Create a new incident in ServiceNow with classification and urgency",
                new {
                    description = McpParam("string", "Incident description", true),
                    urgency     = McpParam("string", "Urgency: 1-High, 2-Medium, 3-Low", false),
                    category    = McpParam("string", "Incident category", false)
                }),

            McpTool("servicenow_triage",
                "Triage an existing ServiceNow incident using AI-powered root cause analysis",
                new { incident_id = McpParam("string", "ServiceNow incident SYS_ID or number", true) }),

            McpTool("hubspot_sync_contact",
                "Sync or update a HubSpot contact with the latest CRM data",
                new {
                    email      = McpParam("string", "Contact email address", true),
                    first_name = McpParam("string", "Contact first name", false),
                    last_name  = McpParam("string", "Contact last name", false)
                }),

            McpTool("stripe_check_billing",
                "Check Stripe billing status and outstanding invoices for a customer",
                new { customer_id = McpParam("string", "Stripe customer ID", true) }),

            McpTool("agent_execute",
                "Execute any Azentix agent task by type and description",
                new {
                    task_type   = McpParam("string", "Task type identifier (e.g. sap-price-sync)", true),
                    description = McpParam("string", "Natural language task description", true)
                }),
        };

        // Expose registered SK plugin functions as MCP tools
        foreach (var plugin in _kernel.Plugins)
        foreach (var fn in plugin)
        {
            var mcpName = $"{plugin.Name.ToLowerInvariant()}_{fn.Name.ToLowerInvariant()}";
            if (tools.Any(t => ((dynamic)t).name == mcpName)) continue;

            var props = fn.Metadata.Parameters.ToDictionary(
                p => p.Name,
                p => (object)McpParam(p.ParameterType?.Name ?? "string", p.Description, p.IsRequired));

            tools.Add(McpTool(mcpName, fn.Description, props));
        }

        return tools;
    }

    private static object McpTool(string name, string description, object properties) => new
    {
        name,
        description,
        inputSchema = new
        {
            type = "object",
            properties,
            required = new string[] { }
        }
    };

    private static object McpParam(string type, string description, bool required) => new
    {
        type        = type.ToLowerInvariant() switch {
            "int32" or "int64" or "int" => "number",
            "boolean" => "boolean",
            _ => "string"
        },
        description,
        required
    };

    private static object McpResult(JsonElement id, object result) => new
    {
        jsonrpc = "2.0",
        id      = id.ValueKind == JsonValueKind.Undefined ? null! : (object)id,
        result
    };

    private static object McpError(int code, string message, JsonElement id = default) => new
    {
        jsonrpc = "2.0",
        id      = id.ValueKind == JsonValueKind.Undefined ? null! : (object)id,
        error   = new { code, message }
    };
}
