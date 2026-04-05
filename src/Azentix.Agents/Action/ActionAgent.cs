using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Action;

public interface IActionAgent
{
    Task<string> ExecuteToolAsync(string toolName, Dictionary<string, object> parameters, CancellationToken ct = default);
}

public class ActionAgent : IActionAgent
{
    private readonly Kernel _kernel;
    private readonly ILogger<ActionAgent> _logger;

    public ActionAgent(Kernel kernel, ILogger<ActionAgent> logger)
    { _kernel = kernel; _logger = logger; }

    public async Task<string> ExecuteToolAsync(string toolName, Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        _logger.LogInformation("ActionAgent: executing tool {Tool}", toolName);
        try
        {
            var parts = toolName.Split('_', 2);
            if (parts.Length < 2) return $"{{\"error\": \"Invalid tool name: {toolName}\"}}";

            var plugin = _kernel.Plugins.FirstOrDefault(p =>
                p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            if (plugin == null) return $"{{\"error\": \"Plugin not found: {parts[0]}\"}}";

            var function = plugin.FirstOrDefault(f =>
                f.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (function == null) return $"{{\"error\": \"Function not found: {toolName}\"}}";

            var args = new KernelArguments();
            foreach (var p in parameters)
                args[p.Key] = p.Value?.ToString();

            var result = await function.InvokeAsync(_kernel, args, ct);
            return result.ToString() ?? "{}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActionAgent tool execution failed: {Tool}", toolName);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }
}
