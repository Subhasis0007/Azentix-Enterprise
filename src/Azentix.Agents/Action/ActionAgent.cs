using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace Azentix.Agents.Action;

public interface IActionAgent
{
    Task<string> RunToolAsync(string toolName,
        Dictionary<string, string> parameters, CancellationToken ct = default);
}

public class ActionAgent : IActionAgent
{
    private readonly Kernel _kernel;
    private readonly ILogger<ActionAgent> _logger;

    public ActionAgent(Kernel kernel, ILogger<ActionAgent> logger)
    { _kernel = kernel; _logger = logger; }

    public async Task<string> RunToolAsync(string toolName,
        Dictionary<string, string> parameters, CancellationToken ct = default)
    {
        _logger.LogInformation("RunTool: {Tool}", toolName);
        var parts = toolName.Split('_', 2);
        if (parts.Length < 2)
            return $"{{\"error\":\"Invalid tool name: {toolName}\"}}";

        var plugin = _kernel.Plugins.FirstOrDefault(p =>
            p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            return $"{{\"error\":\"Plugin not found: {parts[0]}\"}}";

        var fn = plugin.FirstOrDefault(f =>
            f.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
        if (fn is null)
            return $"{{\"error\":\"Function not found: {toolName}\"}}";

        var args = new KernelArguments();
        foreach (var kv in parameters) args[kv.Key] = kv.Value;
        var result = await fn.InvokeAsync(_kernel, args, ct);
        return result.ToString() ?? "{}";
    }
}
