using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Director;

public interface IDirectorAgent
{
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct = default);
}

public class DirectorAgent : IDirectorAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ILogger<DirectorAgent> _logger;
    private readonly AgentConfiguration _config;

    private const string SYSTEM_PROMPT =
        "You are Azentix Director - an enterprise AI agent coordinating SAP, Salesforce, " +
        "ServiceNow, HubSpot, and Stripe integrations. " +
        "Reasoning: THINK -> PLAN -> ACT -> OBSERVE -> CONCLUDE. " +
        "Write Thought: ... then Action: ... wait for Observation: ... " +
        "End with Final Answer: ... " +
        "Tools: sap_get_material, sap_get_price, sap_get_inventory, sap_compare_prices, " +
        "salesforce_get_product, salesforce_get_pricebook, salesforce_update_price, " +
        "salesforce_get_lead, salesforce_update_lead, salesforce_get_opportunity, " +
        "servicenow_get_incident, servicenow_update_incident, servicenow_create_incident, " +
        "servicenow_search_knowledge, hubspot_get_contact, hubspot_create_contact, " +
        "hubspot_update_contact, hubspot_add_to_list, hubspot_get_deal, " +
        "stripe_get_payment, stripe_get_customer, stripe_list_failed_payments, " +
        "rag_search, memory_store, rabbitmq_publish. " +
        "Rules: Always Thought before Action. Never expose PII in logs. " +
        "Validate before writing to any system. Human review if confidence < 0.7.";

    public DirectorAgent(Kernel kernel, ILogger<DirectorAgent> logger, AgentConfiguration config)
    {
        _kernel = kernel;
        _chat = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
        _config = config;
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        _logger.LogInformation("DirectorAgent START: {Id} | {Type}", task.TaskId, task.TaskType);
        var history = new ChatHistory();
        history.AddSystemMessage(SYSTEM_PROMPT);
        history.AddUserMessage(BuildPrompt(task));

        var result = new AgentResult
        {
            TaskId = task.TaskId, StartedAt = DateTime.UtcNow,
            AuditTrail = new List<AuditEntry>(), Status = AgentStatus.Running
        };

        int iterations = 0; bool done = false;
        while (!done && iterations < _config.MaxIterations)
        {
            iterations++;
            try
            {
                var settings = new AzureOpenAIPromptExecutionSettings
                {
                    MaxTokens = _config.MaxTokensPerIteration,
                    Temperature = 0.1,
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };
                var response = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
                history.AddAssistantMessage(response.Content ?? "");

                result.AuditTrail.Add(new AuditEntry {
                    Iteration = iterations,
                    Timestamp = DateTime.UtcNow,
                    AgentThought = Extract(response.Content, "Thought:", "Action:"),
                    AgentAction = Extract(response.Content, "Action:", "Observation:"),
                    TokensUsed = response.Metadata?.GetValueOrDefault("CompletionUsage")?.ToString() ?? "?"
                });

                if (response.Content?.Contains("Final Answer:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    result.FinalAnswer = Extract(response.Content, "Final Answer:", null);
                    result.Status = AgentStatus.Completed;
                    done = true;
                }
                else
                    history.AddUserMessage("Continue. What is your next Thought and Action?");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in iteration {I}", iterations);
                result.Status = AgentStatus.Failed;
                result.ErrorMessage = ex.Message;
                done = true;
            }
        }

        if (iterations >= _config.MaxIterations && !done)
            result.Status = AgentStatus.MaxIterationsReached;

        result.CompletedAt = DateTime.UtcNow;
        result.TotalIterations = iterations;
        _logger.LogInformation("DirectorAgent END: {Id} | {Status} | {I} iters | {Ms}ms",
            task.TaskId, result.Status, iterations, (int)(result.Duration?.TotalMilliseconds ?? 0));
        return result;
    }

    private static string BuildPrompt(AgentTask t) =>
        "TASK: " + t.TaskId + " | TYPE: " + t.TaskType + " | PRIORITY: " + t.Priority +
        "\nDESCRIPTION: " + t.Description +
        "\nCONTEXT: " + t.Context +
        "\nINPUT: " + System.Text.Json.JsonSerializer.Serialize(t.InputData) +
        "\nStart with: Thought: ...";

    private static string Extract(string? content, string start, string? end)
    {
        if (string.IsNullOrEmpty(content)) return "";
        int s = content.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (s < 0) return "";
        s += start.Length;
        if (end == null) return content[s..].Trim();
        int e = content.IndexOf(end, s, StringComparison.OrdinalIgnoreCase);
        return e < 0 ? content[s..].Trim() : content[s..e].Trim();
    }
}
