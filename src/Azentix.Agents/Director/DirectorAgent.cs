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
    private readonly AgentConfiguration _cfg;

    private const string SystemPrompt = @"You are Azentix Director — an enterprise AI agent.
You connect SAP S/4HANA, Salesforce, ServiceNow, HubSpot, and Stripe.
Follow the ReAct pattern strictly:
  Thought: <your reasoning>
  Action: <tool_name>(param1=value1, param2=value2)
  Observation: <result from tool>
  ... repeat until ...
  Final Answer: <your conclusion>

Available tools:
  SAP:         sap_get_material, sap_get_price, sap_get_inventory, sap_compare_prices
  Salesforce:  salesforce_get_product, salesforce_get_pricebook, salesforce_update_price,
               salesforce_get_lead, salesforce_update_lead, salesforce_get_opportunity
  ServiceNow:  servicenow_get_incident, servicenow_update_incident,
               servicenow_create_incident, servicenow_search_knowledge
  HubSpot:     hubspot_get_contact, hubspot_create_contact, hubspot_update_contact,
               hubspot_add_to_list, hubspot_get_deal
  Stripe:      stripe_get_payment, stripe_get_customer, stripe_list_failed_payments
  Platform:    rag_search, rabbitmq_publish

Rules:
- Always write Thought before Action
- Never expose credentials or PII in logs
- Validate data before writing to any system
- If confidence < 0.7 set status to HumanReviewRequired";

    public DirectorAgent(Kernel kernel, ILogger<DirectorAgent> logger, AgentConfiguration cfg)
    {
        _kernel = kernel;
        _chat   = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
        _cfg    = cfg;
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        _logger.LogInformation("START {Id} | {Type} | {Pri}", task.TaskId, task.TaskType, task.Priority);

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(BuildUserMessage(task));

        var result = new AgentResult
        {
            TaskId     = task.TaskId,
            StartedAt  = DateTime.UtcNow,
            Status     = AgentStatus.Running,
            AuditTrail = new List<AuditEntry>()
        };

        int iter = 0;
        while (iter < _cfg.MaxIterations)
        {
            iter++;
            try
            {
                var settings = new AzureOpenAIPromptExecutionSettings
                {
                    MaxTokens        = _cfg.MaxTokensPerIteration,
                    Temperature      = 0.1,
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };
                var response = await _chat.GetChatMessageContentAsync(
                    history, settings, _kernel, ct);
                var text = response.Content ?? string.Empty;
                history.AddAssistantMessage(text);

                result.AuditTrail.Add(new AuditEntry
                {
                    Iteration    = iter,
                    Timestamp    = DateTime.UtcNow,
                    AgentThought = Extract(text, "Thought:", "Action:"),
                    AgentAction  = Extract(text, "Action:",  "Observation:"),
                    TokensUsed   = response.Metadata
                        ?.GetValueOrDefault("CompletionUsage")?.ToString() ?? "?"
                });

                if (text.Contains("Final Answer:", StringComparison.OrdinalIgnoreCase))
                {
                    result.FinalAnswer = Extract(text, "Final Answer:", null);
                    result.Status      = AgentStatus.Completed;
                    break;
                }
                history.AddUserMessage("Continue. What is your next Thought and Action?");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error at iteration {I}", iter);
                result.Status       = AgentStatus.Failed;
                result.ErrorMessage = ex.Message;
                break;
            }
        }

        if (iter >= _cfg.MaxIterations && result.Status == AgentStatus.Running)
            result.Status = AgentStatus.MaxIterationsReached;

        result.CompletedAt     = DateTime.UtcNow;
        result.TotalIterations = iter;
        _logger.LogInformation("END {Id} | {Status} | {I} iters | {Ms}ms",
            task.TaskId, result.Status, iter,
            (int)(result.Duration?.TotalMilliseconds ?? 0));
        return result;
    }

    private static string BuildUserMessage(AgentTask t) =>
        $"TASK_ID: {t.TaskId}\nTYPE: {t.TaskType}\nPRIORITY: {t.Priority}\n" +
        $"DESCRIPTION: {t.Description}\nCONTEXT: {t.Context}\n" +
        $"INPUT: {System.Text.Json.JsonSerializer.Serialize(t.InputData)}\n\n" +
        "Begin. Write your first Thought:";

    private static string Extract(string? text, string start, string? end)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        int s = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (s < 0) return string.Empty;
        s += start.Length;
        if (end == null) return text[s..].Trim();
        int e = text.IndexOf(end, s, StringComparison.OrdinalIgnoreCase);
        return (e < 0 ? text[s..] : text[s..e]).Trim();
    }
}
