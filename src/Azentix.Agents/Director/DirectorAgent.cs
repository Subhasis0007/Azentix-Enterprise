
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Director;

public class DirectorAgent : IDirectorAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ILogger<DirectorAgent> _logger;
    private readonly AgentConfiguration _cfg;
    private readonly SalesforceConfiguration _salesforceConfiguration;
    private readonly ServiceNowConfiguration _serviceNowConfiguration;
    private readonly HubSpotConfiguration _hubSpotConfiguration;
    private readonly StripeConfiguration _stripeConfiguration;
    private readonly bool _isRagEnabled;

    private const string SystemPrompt = @"You are Azentix Director — an enterprise AI agent.
You connect SAP S/4HANA, Salesforce, ServiceNow, HubSpot, and Stripe.
Follow the ReAct pattern strictly:
  Thought: <your reasoning>
  Action: <tool_name>(param=value)
  Observation: <result>
  Final Answer: <your conclusion>

Rules:
- Always write Thought before Action
- Never expose credentials
- Validate data before writing
- If confidence < 0.7 set status to HumanReviewRequired
- Do not claim service/network/auth failures unless your Observation includes the exact tool response that failed
- For sap-salesforce-price-sync, you must run tools before any Final Answer";

    public DirectorAgent(
        Kernel kernel,
        ILogger<DirectorAgent> logger,
        AgentConfiguration cfg,
        SalesforceConfiguration salesforceConfiguration,
        ServiceNowConfiguration serviceNowConfiguration,
        HubSpotConfiguration hubSpotConfiguration,
        StripeConfiguration stripeConfiguration)
    {
        _kernel = kernel;
        _chat   = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
        _cfg    = cfg;
        _salesforceConfiguration = salesforceConfiguration;
        _serviceNowConfiguration = serviceNowConfiguration;
        _hubSpotConfiguration = hubSpotConfiguration;
        _stripeConfiguration = stripeConfiguration;
        _isRagEnabled = cfg.RagEnabled;
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        _logger.LogInformation("START {Id} | {Type} | {Pri}",
            task.TaskId, task.TaskType, task.Priority);

        if (DirectorTaskRules.TryBuildPrevalidatedResult(
            task,
            _salesforceConfiguration,
            _serviceNowConfiguration,
            _hubSpotConfiguration,
            _stripeConfiguration,
            _isRagEnabled,
            out var validationResult))
        {
            _logger.LogInformation("END {Id} | {Status} | prevalidated",
                task.TaskId, validationResult!.Status);
            return validationResult;
        }

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
        var noToolFinalAnswerCount = 0;

        while (iter < _cfg.MaxIterations)
        {
            iter++;
            _logger.LogInformation("ITER {Id} | {Iter}", task.TaskId, iter);

            try
            {
                PromptExecutionSettings settings =
                    _cfg.ModelProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                        ? new OpenAIPromptExecutionSettings
                        {
                            MaxTokens = _cfg.MaxTokensPerIteration,
                            Temperature = 0.1,
                            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                        }
                        : new AzureOpenAIPromptExecutionSettings
                        {
                            MaxTokens = _cfg.MaxTokensPerIteration,
                            Temperature = 0.1,
                            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                        };

                var response = await _chat.GetChatMessageContentAsync(
                    history, settings, _kernel, ct);

                var text = response.Content ?? string.Empty;
                history.AddAssistantMessage(text);

                var thought = Extract(text, "Thought:", "Action:");
                var action = Extract(text, "Action:",  "Observation:");
                var observation = Extract(text, "Observation:", "Final Answer:");

                result.AuditTrail.Add(new AuditEntry
                {
                    Iteration    = iter,
                    Timestamp    = DateTime.UtcNow,
                    AgentThought = thought,
                    AgentAction  = action,
                    ActionResult = observation,
                    TokensUsed   = response.Metadata?
                        .GetValueOrDefault("CompletionUsage")?.ToString() ?? "?"
                });

                _logger.LogInformation("ITER {Id} | {Iter} | Thought={Thought} | Action={Action} | Observation={Observation}",
                    task.TaskId,
                    iter,
                    thought,
                    action,
                    observation);

                if (text.Contains("Final Answer", StringComparison.OrdinalIgnoreCase))
                {
                    var requiresToolExecution = RequiresToolExecution(task.TaskType);
                    var hasAction = !string.IsNullOrWhiteSpace(action);
                    var hasObservation = !string.IsNullOrWhiteSpace(observation);
                    var actionLooksLikeToolCall = LooksLikeToolAction(action);
                    var hasRequiredDomainToolAction = HasSapSalesforceDomainAction(action);

                    if (requiresToolExecution &&
                        (!hasAction ||
                         !hasObservation ||
                         !actionLooksLikeToolCall ||
                         !hasRequiredDomainToolAction))
                    {
                        noToolFinalAnswerCount++;
                        _logger.LogWarning(
                            "ITER {Id} | {Iter} | Final answer blocked. Requires real tool action and observation. Action={Action} | ObservationEmpty={ObservationEmpty} | Count={Count}",
                            task.TaskId,
                            iter,
                            action,
                            string.IsNullOrWhiteSpace(observation),
                            noToolFinalAnswerCount);

                        if (noToolFinalAnswerCount >= 2)
                        {
                            result.Status = AgentStatus.HumanReviewRequired;
                            result.FinalAnswer =
                                "The task was stopped because the director produced final answers without running required tool calls. Review model tool-calling behavior and integration plugin outputs in logs.";
                            break;
                        }

                        history.AddUserMessage(BuildToolExecutionReminder(task.TaskType));
                        continue;
                    }

                    result.FinalAnswer = Extract(text, "Final Answer:", null);
                    result.Status = DirectorTaskRules.InferFinalStatus(text, result.FinalAnswer);
                    break;
                }

                history.AddUserMessage("Continue. What is your next Thought and Action?");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error at iteration {Iter}", iter);
                result.Status       = AgentStatus.Failed;
                result.ErrorMessage = ex.ToString();
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
        BuildTaskSpecificGuidance(t.TaskType) + "\n\n" +
        "Begin. Write your first Thought:";

    private static bool RequiresToolExecution(string taskType) =>
        taskType.Equals("sap-salesforce-price-sync", StringComparison.OrdinalIgnoreCase);

    private static string BuildTaskSpecificGuidance(string taskType)
    {
        if (taskType.Equals("sap-salesforce-price-sync", StringComparison.OrdinalIgnoreCase))
        {
            return "For sap-salesforce-price-sync, call tools in this order before Final Answer: " +
                   "1) sap_get_material 2) salesforce_get_product 3) salesforce_get_pricebook " +
                   "4) salesforce_update_price when governance allows update. " +
                   "If any call fails, include the exact failing tool response in Observation. " +
                   "Do not use rabbitmq_publish for this task; queue handling is external to this execution.";
        }

        return "";
    }

    private static string BuildToolExecutionReminder(string taskType)
    {
        if (taskType.Equals("sap-salesforce-price-sync", StringComparison.OrdinalIgnoreCase))
        {
            return "Your previous response included Final Answer without required tool execution. " +
                   "Run at least sap_get_material, salesforce_get_product, and salesforce_get_pricebook first. " +
                   "Then provide Thought/Action/Observation with the exact tool output. " +
                   "Do not set status directly and do not call rabbitmq_publish.";
        }

        return "Your previous response included Final Answer without required Action/Observation. " +
               "Continue with Thought, Action, and Observation.";
    }

    private static bool LooksLikeToolAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        var normalized = action.Trim();
        return normalized.Contains('(') && normalized.Contains(')');
    }

    private static bool HasSapSalesforceDomainAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        return action.Contains("sap_get_material", StringComparison.OrdinalIgnoreCase) ||
               action.Contains("salesforce_get_product", StringComparison.OrdinalIgnoreCase) ||
               action.Contains("salesforce_get_pricebook", StringComparison.OrdinalIgnoreCase) ||
               action.Contains("salesforce_update_price", StringComparison.OrdinalIgnoreCase);
    }

    private static string Extract(string? text, string start, string? end)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        int s = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (s < 0) return string.Empty;

        s += start.Length;

        if (end == null)
            return text[s..].Trim();

        int e = text.IndexOf(end, s, StringComparison.OrdinalIgnoreCase);
        return (e < 0 ? text[s..] : text[s..e]).Trim();
    }
}
