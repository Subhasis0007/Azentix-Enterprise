
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

        if (task.TaskType.Equals("sap-salesforce-price-sync", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Routing task {Id} to deterministic sap-salesforce-price-sync executor", task.TaskId);
            return await ExecuteSapSalesforcePriceSyncDeterministicallyAsync(task, ct);
        }

        if (task.TaskType.Equals("servicenow-incident-triage", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Routing task {Id} to deterministic servicenow-incident-triage executor", task.TaskId);
            return await ExecuteServiceNowIncidentTriageDeterministicallyAsync(task, ct);
        }

        if (task.TaskType.Equals("stripe-billing-alert", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Routing task {Id} to deterministic stripe-billing-alert executor", task.TaskId);
            return await ExecuteStripeBillingAlertDeterministicallyAsync(task, ct);
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
        var contentFilterRetryCount = 0;

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
                if (IsContentFilterException(ex) && contentFilterRetryCount < 1)
                {
                    contentFilterRetryCount++;
                    _logger.LogWarning(ex,
                        "Content filter triggered at iteration {Iter}. Retrying once with sanitized prompt.",
                        iter);

                    result.AuditTrail.Add(new AuditEntry
                    {
                        Iteration = iter,
                        Timestamp = DateTime.UtcNow,
                        AgentThought = "Azure content filter blocked the prompt. Retrying with sanitized task context.",
                        AgentAction = "sanitize_prompt_and_retry(task)",
                        ActionResult = "content_filter",
                        TokensUsed = "0"
                    });

                    history = new ChatHistory();
                    history.AddSystemMessage(SystemPrompt);
                    history.AddUserMessage(BuildSanitizedUserMessage(task));
                    continue;
                }

                if (IsContentFilterException(ex))
                {
                    _logger.LogError(ex,
                        "Content filter persisted after sanitized retry at iteration {Iter}",
                        iter);
                    result.Status = AgentStatus.HumanReviewRequired;
                    result.FinalAnswer =
                        "Execution was blocked by Azure OpenAI content filtering after a sanitized retry. Review task text/context and retry.";
                    result.ErrorMessage = "Azure OpenAI content filter blocked the request after retry.";
                    break;
                }

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

    private async Task<AgentResult> ExecuteSapSalesforcePriceSyncDeterministicallyAsync(AgentTask task, CancellationToken ct)
    {
        var result = new AgentResult
        {
            TaskId = task.TaskId,
            StartedAt = DateTime.UtcNow,
            Status = AgentStatus.Running,
            AuditTrail = new List<AuditEntry>()
        };

        var sapMaterialNumber = GetInputString(task.InputData, "sapMaterialNumber");
        var salesforceProductId = GetInputString(task.InputData, "salesforceProductId");

        if (string.IsNullOrWhiteSpace(sapMaterialNumber) || string.IsNullOrWhiteSpace(salesforceProductId))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "Missing required input: sapMaterialNumber and salesforceProductId are required.";
            result.AuditTrail.Add(new AuditEntry
            {
                Iteration = 1,
                Timestamp = DateTime.UtcNow,
                AgentThought = "Cannot execute price sync because required identifiers are missing.",
                AgentAction = "validate_input(sapMaterialNumber, salesforceProductId)",
                ActionResult = result.FinalAnswer,
                TokensUsed = "0"
            });
            return await FinalizeDeterministicResultWithLlmAsync(task, result, 1, ct);
        }

        var step = 0;

        step++;
        var sapMaterialResponse = await InvokeKernelToolAsync(
            "sap_get_material",
            new Dictionary<string, object>
            {
                ["materialNumber"] = sapMaterialNumber
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Fetch SAP material context for the requested material number.",
            AgentAction = $"sap_get_material(materialNumber={sapMaterialNumber})",
            ActionResult = sapMaterialResponse,
            TokensUsed = "0"
        });

        step++;
        var productResponse = await InvokeKernelToolAsync(
            "salesforce_get_product",
            new Dictionary<string, object>
            {
                ["identifier"] = salesforceProductId,
                ["isId"] = true
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Retrieve Salesforce Product2 to validate connectivity and product existence.",
            AgentAction = $"salesforce_get_product(identifier={salesforceProductId}, isId=true)",
            ActionResult = productResponse,
            TokensUsed = "0"
        });

        if (!IsSuccessfulSalesforcePayload(productResponse, out var productFailureReason))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer =
                "Salesforce product lookup failed. Review connectivity/auth and product ID before retrying.";
            result.ErrorMessage = productFailureReason;
            return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
        }

        step++;
        var pricebookResponse = await InvokeKernelToolAsync(
            "salesforce_get_pricebook",
            new Dictionary<string, object>
            {
                ["productId"] = salesforceProductId
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Retrieve Standard PricebookEntry for the Salesforce product.",
            AgentAction = $"salesforce_get_pricebook(productId={salesforceProductId})",
            ActionResult = pricebookResponse,
            TokensUsed = "0"
        });

        if (!IsSuccessfulSalesforcePayload(pricebookResponse, out var pricebookFailureReason))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer =
                "Salesforce standard pricebook lookup failed. Ensure an active Standard PricebookEntry exists for this product.";
            result.ErrorMessage = pricebookFailureReason;
            return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
        }

        var currentPrice = TryExtractUnitPrice(pricebookResponse);
        result.OutputData["sapMaterialNumber"] = sapMaterialNumber;
        result.OutputData["salesforceProductId"] = salesforceProductId;
        if (currentPrice is not null)
            result.OutputData["currentSalesforcePrice"] = currentPrice.Value;

        result.Status = AgentStatus.Completed;
        result.FinalAnswer =
            currentPrice is null
                ? "Salesforce authentication and product retrieval succeeded, and Standard PricebookEntry was found."
                : $"Salesforce authentication and product retrieval succeeded. Current Standard Pricebook price is {currentPrice.Value}.";
        return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
    }

    private async Task<AgentResult> ExecuteServiceNowIncidentTriageDeterministicallyAsync(AgentTask task, CancellationToken ct)
    {
        var result = new AgentResult
        {
            TaskId = task.TaskId,
            StartedAt = DateTime.UtcNow,
            Status = AgentStatus.Running,
            AuditTrail = new List<AuditEntry>()
        };

        var incidentNumber = GetInputString(task.InputData, "incidentNumber");
        if (string.IsNullOrWhiteSpace(incidentNumber))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "Missing required input: incidentNumber.";
            result.ErrorMessage = "incidentNumber is required.";
            result.AuditTrail.Add(new AuditEntry
            {
                Iteration = 1,
                Timestamp = DateTime.UtcNow,
                AgentThought = "Cannot triage incident because incidentNumber was not provided.",
                AgentAction = "validate_input(incidentNumber)",
                ActionResult = result.ErrorMessage,
                TokensUsed = "0"
            });
            return await FinalizeDeterministicResultWithLlmAsync(task, result, 1, ct);
        }

        var step = 0;

        step++;
        var getIncidentResponse = await InvokeKernelToolAsync(
            "servicenow_get_incident",
            new Dictionary<string, object>
            {
                ["id"] = incidentNumber,
                ["isNumber"] = true
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Retrieve incident details from ServiceNow.",
            AgentAction = $"servicenow_get_incident(id={incidentNumber}, isNumber=true)",
            ActionResult = getIncidentResponse,
            TokensUsed = "0"
        });

        if (!IsSuccessfulServiceNowPayload(getIncidentResponse, out var incidentFailureReason))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "ServiceNow incident retrieval failed. Verify instance URL, username, and password.";
            result.ErrorMessage = incidentFailureReason;
            return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
        }

        if (!TryExtractServiceNowIncident(getIncidentResponse, out var incidentSysId, out var shortDescription))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "ServiceNow incident was not found or did not return a valid result record.";
            result.ErrorMessage = getIncidentResponse;
            return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
        }

        var knowledgeQuery = string.IsNullOrWhiteSpace(shortDescription)
            ? incidentNumber
            : shortDescription!;

        step++;
        var kbResponse = await InvokeKernelToolAsync(
            "servicenow_search_knowledge",
            new Dictionary<string, object>
            {
                ["query"] = knowledgeQuery,
                ["limit"] = 3
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Search ServiceNow KB for related guidance.",
            AgentAction = "servicenow_search_knowledge(query=<incident summary>, limit=3)",
            ActionResult = kbResponse,
            TokensUsed = "0"
        });

        var assignmentGroup = DetermineAssignmentGroup(shortDescription);
        var triageNote = $"Incident {incidentNumber} triaged by Azentix. Routed to {assignmentGroup} after KB similarity review.";

        step++;
        var updateResponse = await InvokeKernelToolAsync(
            "servicenow_update_incident",
            new Dictionary<string, object>
            {
                ["sysId"] = incidentSysId!,
                ["state"] = "2",
                ["priority"] = "2",
                ["assignmentGroup"] = assignmentGroup,
                ["workNote"] = triageNote
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Apply assignment update and append triage work note.",
            AgentAction = $"servicenow_update_incident(sysId={incidentSysId}, state=2, priority=2, assignmentGroup={assignmentGroup})",
            ActionResult = updateResponse,
            TokensUsed = "0"
        });

        if (!IsSuccessfulServiceNowPayload(updateResponse, out var updateFailureReason))
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "ServiceNow incident update failed during triage. Review API response details for credentials/ACL issues.";
            result.ErrorMessage = updateFailureReason;
            return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
        }

        result.OutputData["incidentNumber"] = incidentNumber;
        result.OutputData["incidentSysId"] = incidentSysId!;
        result.OutputData["assignmentGroup"] = assignmentGroup;
        result.OutputData["updateDetails"] = TryParseJsonElement(updateResponse);

        result.Status = AgentStatus.Completed;
        result.FinalAnswer =
            $"The ServiceNow incident '{incidentNumber}' was triaged and updated successfully. Assignment group was set to '{assignmentGroup}' and a work note was appended.";

        return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
    }

    private async Task<AgentResult> ExecuteStripeBillingAlertDeterministicallyAsync(AgentTask task, CancellationToken ct)
    {
        var result = new AgentResult
        {
            TaskId = task.TaskId,
            StartedAt = DateTime.UtcNow,
            Status = AgentStatus.Running,
            AuditTrail = new List<AuditEntry>()
        };

        var stripeEventType = GetInputString(task.InputData, "stripeEventType") ?? "unknown";
        var stripeEventId = GetInputString(task.InputData, "stripeEventId") ?? "unknown";
        var stripeCustomerId = GetInputString(task.InputData, "stripeCustomerId") ?? "unknown";
        var salesforceAccountId = GetInputString(task.InputData, "salesforceAccountId") ?? "unknown";
        var hubspotContactId = GetInputString(task.InputData, "hubspotContactId") ?? "unknown";

        var step = 0;

        step++;
        var createIncidentResponse = await InvokeKernelToolAsync(
            "servicenow_create_incident",
            new Dictionary<string, object>
            {
                ["shortDescription"] = $"Stripe payment failure: {stripeEventId}",
                ["description"] =
                    $"Stripe payment failure detected. Event={stripeEventId}; Type={stripeEventType}; Customer={stripeCustomerId}; SalesforceAccount={salesforceAccountId}; HubSpotContact={hubspotContactId}.",
                ["category"] = "stripe",
                ["urgency"] = "2",
                ["impact"] = "2",
                ["assignmentGroup"] = "Finance-Operations"
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Create ServiceNow incident for Stripe payment failure.",
            AgentAction = "servicenow_create_incident(...)",
            ActionResult = createIncidentResponse,
            TokensUsed = "0"
        });

        var incidentNumber = TryExtractServiceNowCreatedIncidentNumber(createIncidentResponse);
        var incidentSysId = TryExtractServiceNowCreatedIncidentSysId(createIncidentResponse);

        step++;
        var openOppsResponse = await InvokeKernelToolAsync(
            "salesforce_get_open_opportunities_by_account",
            new Dictionary<string, object>
            {
                ["accountId"] = salesforceAccountId,
                ["limit"] = 5
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Check open Salesforce opportunities for the provided account.",
            AgentAction = $"salesforce_get_open_opportunities_by_account(accountId={salesforceAccountId}, limit=5)",
            ActionResult = openOppsResponse,
            TokensUsed = "0"
        });

        var salesforceSuccess = IsSuccessfulSalesforcePayload(openOppsResponse, out var salesforceFailureReason);
        var opportunityCount = TryExtractSalesforceOpportunityCount(openOppsResponse);

        step++;
        var hubspotUpdateResponse = await InvokeKernelToolAsync(
            "hubspot_update_contact",
            new Dictionary<string, object>
            {
                ["contactId"] = hubspotContactId,
                ["propertiesJson"] = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["hs_lead_status"] = "UNQUALIFIED"
                })
            },
            ct);
        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = step,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Update HubSpot contact to reflect payment failure state.",
            AgentAction = $"hubspot_update_contact(contactId={hubspotContactId}, propertiesJson=...)",
            ActionResult = hubspotUpdateResponse,
            TokensUsed = "0"
        });

        var hubspotSuccess = IsSuccessfulHubSpotPayload(hubspotUpdateResponse, out var hubspotFailureReason);

        var createdIncidentDisplay = string.IsNullOrWhiteSpace(incidentNumber)
            ? "not returned"
            : incidentNumber;

        var salesforceOutcome = salesforceSuccess
            ? $"open opportunities found: {opportunityCount}"
            : $"lookup failed ({salesforceFailureReason})";

        var hubspotOutcome = hubspotSuccess
            ? "contact update succeeded"
            : $"contact update failed ({hubspotFailureReason})";

        result.OutputData["stripeEventId"] = stripeEventId;
        result.OutputData["createdIncidentNumber"] = createdIncidentDisplay;
        result.OutputData["createdIncidentSysId"] = incidentSysId ?? string.Empty;
        result.OutputData["salesforceOpportunityCount"] = opportunityCount;
        result.OutputData["salesforceLookupSuccess"] = salesforceSuccess;
        result.OutputData["salesforceDetails"] = TryParseJsonElement(openOppsResponse);
        result.OutputData["hubspotUpdateSuccess"] = hubspotSuccess;
        result.OutputData["hubspotDetails"] = TryParseJsonElement(hubspotUpdateResponse);

        result.Status = AgentStatus.Completed;
        result.FinalAnswer =
            $"Stripe billing alert handling finished. ServiceNow incident created: {createdIncidentDisplay}. " +
            $"Salesforce outcome: {salesforceOutcome}. HubSpot outcome: {hubspotOutcome}.";

        return await FinalizeDeterministicResultWithLlmAsync(task, result, step, ct);
    }

    private async Task<AgentResult> FinalizeDeterministicResultWithLlmAsync(
        AgentTask task,
        AgentResult result,
        int lastIteration,
        CancellationToken ct)
    {
        var llmLayer = await TryBuildLlmPresentationAsync(task, result, ct);
        var llmIteration = lastIteration + 1;

        result.AuditTrail.Add(new AuditEntry
        {
            Iteration = llmIteration,
            Timestamp = DateTime.UtcNow,
            AgentThought = "Generate polished business-facing summary from deterministic tool results.",
            AgentAction = "llm_generate_summary()",
            ActionResult = llmLayer.AuditResult,
            TokensUsed = llmLayer.TokensUsed
        });

        if (llmLayer.Filtered)
        {
            result.Status = AgentStatus.HumanReviewRequired;
            result.FinalAnswer = "Execution requires human review because the LLM output stage was blocked by content filtering.";
            result.ErrorMessage = llmLayer.ErrorMessage;
            result.OutputData["llmLayerStatus"] = "content_filter_blocked";
            result.OutputData["llmLayerError"] = llmLayer.ErrorMessage ?? "content_filter";
        }
        else if (!string.IsNullOrWhiteSpace(llmLayer.SummaryText))
        {
            result.FinalAnswer = llmLayer.SummaryText;
            result.OutputData["llmLayerStatus"] = "applied";
            result.OutputData["llmSummary"] = llmLayer.SummaryText;
        }
        else
        {
            result.OutputData["llmLayerStatus"] = "fallback_to_deterministic";
        }

        result.CompletedAt = DateTime.UtcNow;
        result.TotalIterations = llmIteration;
        return result;
    }

    private async Task<LlmSummaryOutcome> TryBuildLlmPresentationAsync(AgentTask task, AgentResult result, CancellationToken ct)
    {
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage("You are an enterprise integration assistant. Rewrite the provided execution outcome into a concise, professional status update. Keep facts unchanged. Never fabricate steps.");

            var deterministicAnswer = result.FinalAnswer ?? "No deterministic answer available.";
            var errorSummary = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "none"
                : result.ErrorMessage!;

            history.AddUserMessage(
                $"TaskType: {task.TaskType}\n" +
                $"TaskId: {task.TaskId}\n" +
                $"Status: {result.Status}\n" +
                $"DeterministicAnswer: {deterministicAnswer}\n" +
                $"ErrorSummary: {errorSummary}\n" +
                BuildLlmFormattingInstruction(task.TaskType));

            PromptExecutionSettings settings =
                _cfg.ModelProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                    ? new OpenAIPromptExecutionSettings
                    {
                        MaxTokens = Math.Min(_cfg.MaxTokensPerIteration, 300),
                        Temperature = 0
                    }
                    : new AzureOpenAIPromptExecutionSettings
                    {
                        MaxTokens = Math.Min(_cfg.MaxTokensPerIteration, 300),
                        Temperature = 0
                    };

            var response = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
            var text = response.Content?.Trim();
            var tokens = response.Metadata?
                .GetValueOrDefault("CompletionUsage")?.ToString() ?? "?";

            if (string.IsNullOrWhiteSpace(text))
            {
                return new LlmSummaryOutcome
                {
                    SummaryText = null,
                    Filtered = false,
                    ErrorMessage = "LLM returned empty summary.",
                    AuditResult = "empty_summary",
                    TokensUsed = tokens
                };
            }

            return new LlmSummaryOutcome
            {
                SummaryText = text,
                Filtered = false,
                ErrorMessage = null,
                AuditResult = text,
                TokensUsed = tokens
            };
        }
        catch (Exception ex) when (IsContentFilterException(ex))
        {
            var detail = ExtractContentFilterDetail(ex);
            return new LlmSummaryOutcome
            {
                SummaryText = null,
                Filtered = true,
                ErrorMessage = detail,
                AuditResult = $"content_filter: {detail}",
                TokensUsed = "0"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM presentation layer failed; using deterministic answer.");
            return new LlmSummaryOutcome
            {
                SummaryText = null,
                Filtered = false,
                ErrorMessage = ex.Message,
                AuditResult = $"llm_error: {ex.Message}",
                TokensUsed = "0"
            };
        }
    }

    private async Task<string> InvokeKernelToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken ct)
    {
        var parts = toolName.Split('_', 2);
        if (parts.Length < 2)
            return JsonSerializer.Serialize(new { success = false, error = $"Invalid tool name: {toolName}" });

        var plugin = _kernel.Plugins.FirstOrDefault(p =>
            p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Plugin not found: {parts[0]}" });

        var fn = plugin.FirstOrDefault(f =>
            f.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
        if (fn is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Function not found: {toolName}" });

        var args = new KernelArguments();
        foreach (var kv in parameters)
            args[kv.Key] = kv.Value;

        var functionResult = await fn.InvokeAsync(_kernel, args, ct);
        return functionResult.ToString() ?? "{}";
    }

    private static string? GetInputString(Dictionary<string, object> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }

    private static bool IsSuccessfulSalesforcePayload(string payload, out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryParseJson(payload, out var root))
        {
            failureReason = "Tool output was not valid JSON.";
            return false;
        }

        if (!root.TryGetProperty("success", out var successElement))
            return true;

        if (successElement.ValueKind == JsonValueKind.True)
            return true;

        failureReason = payload;
        return false;
    }

    private static bool IsSuccessfulServiceNowPayload(string payload, out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryParseJson(payload, out var root))
        {
            failureReason = "Tool output was not valid JSON.";
            return false;
        }

        if (!root.TryGetProperty("success", out var successElement))
            return true;

        if (successElement.ValueKind == JsonValueKind.True)
            return true;

        failureReason = payload;
        return false;
    }

    private static bool IsSuccessfulHubSpotPayload(string payload, out string failureReason)
    {
        failureReason = string.Empty;
        if (!TryParseJson(payload, out var root))
        {
            failureReason = "Tool output was not valid JSON.";
            return false;
        }

        if (!root.TryGetProperty("success", out var successElement))
            return true;

        if (successElement.ValueKind == JsonValueKind.True)
            return true;

        failureReason = payload;
        return false;
    }

    private static string? TryExtractServiceNowCreatedIncidentNumber(string payload)
    {
        if (!TryParseJson(payload, out var root) ||
            !root.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object)
            return null;

        return result.TryGetProperty("number", out var number)
            ? number.GetString()
            : null;
    }

    private static string? TryExtractServiceNowCreatedIncidentSysId(string payload)
    {
        if (!TryParseJson(payload, out var root) ||
            !root.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object)
            return null;

        return result.TryGetProperty("sys_id", out var sysId)
            ? sysId.GetString()
            : null;
    }

    private static int TryExtractSalesforceOpportunityCount(string payload)
    {
        if (!TryParseJson(payload, out var root) ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return 0;

        if (data.TryGetProperty("totalSize", out var totalSize) && totalSize.TryGetInt32(out var count))
            return count;

        return 0;
    }

    private static string BuildLlmFormattingInstruction(string taskType)
    {
        if (taskType.Equals("stripe-billing-alert", StringComparison.OrdinalIgnoreCase))
        {
            return "Return 3-5 short sentences. Include: created ServiceNow incident number, Salesforce opportunity result, HubSpot update result, and a clear next action if any integration step failed.";
        }

        return "Return 2-4 short sentences. Include root cause when status is HumanReviewRequired.";
    }

    private static bool TryExtractServiceNowIncident(
        string getIncidentPayload,
        out string? sysId,
        out string? shortDescription)
    {
        sysId = null;
        shortDescription = null;

        if (!TryParseJson(getIncidentPayload, out var root))
            return false;

        if (!root.TryGetProperty("data", out var dataElement))
            return false;

        if (!dataElement.TryGetProperty("result", out var resultElement))
            return false;

        JsonElement incidentRecord;
        if (resultElement.ValueKind == JsonValueKind.Array)
        {
            if (resultElement.GetArrayLength() == 0)
                return false;

            incidentRecord = resultElement[0];
        }
        else if (resultElement.ValueKind == JsonValueKind.Object)
        {
            incidentRecord = resultElement;
        }
        else
        {
            return false;
        }

        if (incidentRecord.TryGetProperty("sys_id", out var sysIdElement))
            sysId = sysIdElement.GetString();

        if (incidentRecord.TryGetProperty("short_description", out var shortDescriptionElement))
            shortDescription = shortDescriptionElement.GetString();

        return !string.IsNullOrWhiteSpace(sysId);
    }

    private static string DetermineAssignmentGroup(string? shortDescription)
    {
        if (string.IsNullOrWhiteSpace(shortDescription))
            return "IT Support";

        var text = shortDescription.ToLowerInvariant();
        if (text.Contains("sap")) return "SAP-Basis";
        if (text.Contains("salesforce") || text.Contains("crm")) return "CRM-Integration";
        if (text.Contains("network") || text.Contains("dns") || text.Contains("latency")) return "Network-Ops";
        if (text.Contains("payment") || text.Contains("stripe")) return "Finance-Operations";

        return "IT Support";
    }

    private static decimal? TryExtractUnitPrice(string pricebookPayload)
    {
        if (!TryParseJson(pricebookPayload, out var root))
            return null;

        if (!root.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("records", out var recordsElement) ||
            recordsElement.ValueKind != JsonValueKind.Array ||
            recordsElement.GetArrayLength() == 0)
            return null;

        var firstRecord = recordsElement[0];
        if (!firstRecord.TryGetProperty("UnitPrice", out var unitPriceElement))
            return null;

        if (unitPriceElement.ValueKind == JsonValueKind.Number &&
            unitPriceElement.TryGetDecimal(out var numericPrice))
            return numericPrice;

        if (unitPriceElement.ValueKind == JsonValueKind.String &&
            decimal.TryParse(unitPriceElement.GetString(), out var parsedPrice))
            return parsedPrice;

        return null;
    }

    private static bool TryParseJson(string value, out JsonElement root)
    {
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(value);
            return true;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static object TryParseJsonElement(string value)
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

    private static string BuildUserMessage(AgentTask t) =>
        $"TASK_ID: {t.TaskId}\nTYPE: {t.TaskType}\nPRIORITY: {t.Priority}\n" +
        $"DESCRIPTION: {t.Description}\nCONTEXT: {t.Context}\n" +
        $"INPUT: {System.Text.Json.JsonSerializer.Serialize(t.InputData)}\n\n" +
        BuildTaskSpecificGuidance(t.TaskType) + "\n\n" +
        "Begin. Write your first Thought:";

    private static string BuildSanitizedUserMessage(AgentTask t) =>
        $"TASK_ID: {t.TaskId}\nTYPE: {t.TaskType}\nPRIORITY: {t.Priority}\n" +
        $"INPUT: {System.Text.Json.JsonSerializer.Serialize(t.InputData)}\n\n" +
        BuildTaskSpecificGuidance(t.TaskType) + "\n\n" +
        "The prior request was blocked by policy filter. Continue with neutral, concise Thought/Action/Observation and factual tool output only.";

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

    private static bool IsContentFilterException(Exception ex) =>
        ex.ToString().Contains("content_filter", StringComparison.OrdinalIgnoreCase);

    private static string ExtractContentFilterDetail(Exception ex)
    {
        var raw = ex.ToString();
        var firstLine = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        var detail = string.IsNullOrWhiteSpace(firstLine)
            ? "Azure OpenAI content filter blocked the LLM output stage."
            : firstLine.Trim();

        if (detail.Length > 300)
            detail = detail[..300] + "...";

        return detail;
    }

    private sealed class LlmSummaryOutcome
    {
        public string? SummaryText { get; init; }
        public bool Filtered { get; init; }
        public string? ErrorMessage { get; init; }
        public string AuditResult { get; init; } = string.Empty;
        public string TokensUsed { get; init; } = "0";
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
