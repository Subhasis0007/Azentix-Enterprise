
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
- If confidence < 0.7 set status to HumanReviewRequired";

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

        while (iter < _cfg.MaxIterations)
        {
            iter++;

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

                result.AuditTrail.Add(new AuditEntry
                {
                    Iteration    = iter,
                    Timestamp    = DateTime.UtcNow,
                    AgentThought = Extract(text, "Thought:", "Action:"),
                    AgentAction  = Extract(text, "Action:",  "Observation:"),
                    TokensUsed   = response.Metadata?
                        .GetValueOrDefault("CompletionUsage")?.ToString() ?? "?"
                });

                if (text.Contains("Final Answer", StringComparison.OrdinalIgnoreCase))
                {
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

        if (end == null)
            return text[s..].Trim();

        int e = text.IndexOf(end, s, StringComparison.OrdinalIgnoreCase);
        return (e < 0 ? text[s..] : text[s..e]).Trim();
    }
}
