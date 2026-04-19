using System.Text.Json;
using System.Text.RegularExpressions;
using Azentix.Models;

namespace Azentix.Agents.Director;

public static partial class DirectorTaskRules
{
    private static readonly string[] PlaceholderValues =
    [
        "your_key_here",
        "your_admin_password",
        "your_consumer_key",
        "your_consumer_secret",
        "your@email.com",
        "yourpasswordplussecuritytoken",
        "change-this",
        "placeholder"
    ];

    private static readonly string[] HumanReviewSignals =
    [
        "humanreviewrequired",
        "human review",
        "manual review",
        "needs approval",
        "approval required",
        "requires approval",
        "marked for human review"
    ];

    public static AgentStatus InferFinalStatus(string? responseText, string? finalAnswer)
    {
        var combined = $"{responseText}\n{finalAnswer}";
        return ContainsHumanReviewSignal(combined)
            ? AgentStatus.HumanReviewRequired
            : AgentStatus.Completed;
    }

    public static bool TryBuildPrevalidatedResult(
        AgentTask task,
        SalesforceConfiguration? salesforceConfiguration,
        ServiceNowConfiguration? serviceNowConfiguration,
        HubSpotConfiguration? hubSpotConfiguration,
        StripeConfiguration? stripeConfiguration,
        bool isRagEnabled,
        out AgentResult? result)
    {
        result = null;

        if (task.TaskType.Equals("sap-salesforce-price-sync", StringComparison.OrdinalIgnoreCase))
            return TryBuildSapSalesforcePriceSyncResult(task, salesforceConfiguration, out result);

        if (task.TaskType.Equals("servicenow-incident-triage", StringComparison.OrdinalIgnoreCase))
            return TryBuildServiceNowIncidentTriageResult(
                task,
                serviceNowConfiguration,
                isRagEnabled,
                out result);

        if (task.TaskType.Equals("stripe-billing-alert", StringComparison.OrdinalIgnoreCase))
            return TryBuildStripeBillingAlertResult(
                task,
                serviceNowConfiguration,
                salesforceConfiguration,
                hubSpotConfiguration,
                stripeConfiguration,
                out result);

        return false;
    }

    private static bool TryBuildStripeBillingAlertResult(
        AgentTask task,
        ServiceNowConfiguration? serviceNowConfiguration,
        SalesforceConfiguration? salesforceConfiguration,
        HubSpotConfiguration? hubSpotConfiguration,
        StripeConfiguration? stripeConfiguration,
        out AgentResult? result)
    {
        result = null;

        var stripeEventType = GetInputString(task.InputData, "stripeEventType");
        var stripeEventId = GetInputString(task.InputData, "stripeEventId");
        var stripeCustomerId = GetInputString(task.InputData, "stripeCustomerId");
        var salesforceAccountId = GetInputString(task.InputData, "salesforceAccountId");
        var hubspotContactId = GetInputString(task.InputData, "hubspotContactId");

        if (string.IsNullOrWhiteSpace(stripeEventType) ||
            !stripeEventType.Contains("payment_failed", StringComparison.OrdinalIgnoreCase))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because stripeEventType is missing or not a payment failure event.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(stripeEventId) || !LooksLikeStripeEventId(stripeEventId))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because stripeEventId is missing or not a valid Stripe event identifier.");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(stripeCustomerId) && !LooksLikeStripeCustomerId(stripeCustomerId))
        {
            result = CreateHumanReviewResult(
                task,
                $"The task cannot proceed because stripeCustomerId '{stripeCustomerId}' is not a valid Stripe customer identifier.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(salesforceAccountId) || LooksLikePlaceholderSalesforceAccountId(salesforceAccountId))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because salesforceAccountId is missing or a placeholder. Provide a real Salesforce Account ID.");
            return true;
        }

        if (!LooksLikeSalesforceAccountId(salesforceAccountId))
        {
            result = CreateHumanReviewResult(
                task,
                $"The task cannot proceed because salesforceAccountId '{salesforceAccountId}' is not a valid Salesforce Account ID format.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(hubspotContactId) || !LooksLikeHubSpotContactId(hubspotContactId))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because hubspotContactId is missing or invalid.");
            return true;
        }

        if (serviceNowConfiguration is not null)
        {
            if (LooksLikePlaceholderUrl(serviceNowConfiguration.InstanceUrl) ||
                HasPlaceholderValue(serviceNowConfiguration.Username, serviceNowConfiguration.Password))
            {
                result = CreateHumanReviewResult(
                    task,
                    "The task cannot proceed because ServiceNow integration settings are missing or placeholders.");
                return true;
            }
        }

        if (salesforceConfiguration is not null)
        {
            if (LooksLikePlaceholderUrl(salesforceConfiguration.InstanceUrl) ||
                HasPlaceholderValue(
                    salesforceConfiguration.ClientId,
                    salesforceConfiguration.ClientSecret,
                    salesforceConfiguration.Username,
                    salesforceConfiguration.Password))
            {
                result = CreateHumanReviewResult(
                    task,
                    "The task cannot proceed because Salesforce integration settings are missing or placeholders.");
                return true;
            }
        }

        if (hubSpotConfiguration is not null)
        {
            if (LooksLikePlaceholderUrl(hubSpotConfiguration.ApiBase) ||
                HasPlaceholderValue(hubSpotConfiguration.AccessToken))
            {
                result = CreateHumanReviewResult(
                    task,
                    "The task cannot proceed because HubSpot integration settings are missing or placeholders.");
                return true;
            }
        }

        if (stripeConfiguration is not null && HasPlaceholderValue(stripeConfiguration.SecretKey))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because Stripe integration settings are missing or placeholders.");
            return true;
        }

        return false;
    }

    private static bool TryBuildSapSalesforcePriceSyncResult(
        AgentTask task,
        SalesforceConfiguration? salesforceConfiguration,
        out AgentResult? result)
    {
        result = null;

        if (salesforceConfiguration is not null && LooksLikePlaceholderUrl(salesforceConfiguration.InstanceUrl))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because the Salesforce integration is not configured with a real instance URL. This task is marked for human review.");
            return true;
        }

        if (salesforceConfiguration is not null && HasPlaceholderValue(
                salesforceConfiguration.ClientId,
                salesforceConfiguration.ClientSecret,
                salesforceConfiguration.Username,
                salesforceConfiguration.Password))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because the Salesforce integration credentials are missing or still using placeholder values. This task is marked for human review.");
            return true;
        }

        var salesforceProductId = GetInputString(task.InputData, "salesforceProductId");

        if (string.IsNullOrWhiteSpace(salesforceProductId))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because salesforceProductId is missing. This task is marked for human review due to missing Salesforce product information.");
            return true;
        }

        if (LooksLikePlaceholderSalesforceProductId(salesforceProductId))
        {
            result = CreateHumanReviewResult(
                task,
                $"The task cannot proceed because the Salesforce product ID '{salesforceProductId}' is a placeholder value. Provide a real Product2 ID before retrying.");
            return true;
        }

        if (!LooksLikeSalesforceProductId(salesforceProductId))
        {
            result = CreateHumanReviewResult(
                task,
                $"The task cannot proceed because the Salesforce product ID '{salesforceProductId}' is not a valid Product2 ID format. This task is marked for human review.");
            return true;
        }

        return false;
    }

    private static bool TryBuildServiceNowIncidentTriageResult(
        AgentTask task,
        ServiceNowConfiguration? serviceNowConfiguration,
        bool isRagEnabled,
        out AgentResult? result)
    {
        result = null;

        var incidentNumber = GetInputString(task.InputData, "incidentNumber");

        if (string.IsNullOrWhiteSpace(incidentNumber))
        {
            result = CreateHumanReviewResult(
                task,
                "The task cannot proceed because incidentNumber is missing. This task is marked for human review due to missing incident information.");
            return true;
        }

        if (!LooksLikeServiceNowIncidentNumber(incidentNumber))
        {
            result = CreateHumanReviewResult(
                task,
                $"The task cannot proceed because the incident number '{incidentNumber}' is not a valid ServiceNow incident format. This task is marked for human review.");
            return true;
        }

        if (!isRagEnabled)
        {
            result = CreateHumanReviewResult(
                task,
                "The triage process cannot proceed because RAG is disabled for the current runtime. Configure an embedding deployment to enable knowledge-base retrieval for incident triage.");
            return true;
        }

        if (serviceNowConfiguration is null)
            return false;

        if (LooksLikePlaceholderUrl(serviceNowConfiguration.InstanceUrl))
        {
            result = CreateHumanReviewResult(
                task,
                "The triage process cannot proceed because the ServiceNow instance URL is missing or still using a placeholder value. Configure SERVICENOW_INSTANCE_URL with a real instance before retrying.");
            return true;
        }

        if (HasPlaceholderValue(serviceNowConfiguration.Username, serviceNowConfiguration.Password))
        {
            result = CreateHumanReviewResult(
                task,
                "The triage process cannot proceed because the ServiceNow integration credentials are missing or still using placeholder values. Configure SERVICENOW_USERNAME and SERVICENOW_PASSWORD before retrying.");
            return true;
        }

        return false;
    }

    public static bool LooksLikeSalesforceProductId(string productId) =>
        SalesforceProductIdPattern().IsMatch(productId.Trim());

    public static bool LooksLikeServiceNowIncidentNumber(string incidentNumber) =>
        ServiceNowIncidentNumberPattern().IsMatch(incidentNumber.Trim());

    public static bool LooksLikeSalesforceAccountId(string accountId) =>
        SalesforceAccountIdPattern().IsMatch(accountId.Trim());

    public static bool LooksLikeHubSpotContactId(string contactId) =>
        HubSpotContactIdPattern().IsMatch(contactId.Trim());

    public static bool LooksLikeStripeEventId(string eventId) =>
        StripeEventIdPattern().IsMatch(eventId.Trim());

    public static bool LooksLikeStripeCustomerId(string customerId) =>
        StripeCustomerIdPattern().IsMatch(customerId.Trim());

    public static bool LooksLikePlaceholderSalesforceProductId(string productId)
    {
        var trimmed = productId.Trim();
        return trimmed.Contains("XXX", StringComparison.OrdinalIgnoreCase) ||
               PlaceholderSalesforceProductIdPattern().IsMatch(trimmed);
    }

    public static bool LooksLikePlaceholderSalesforceAccountId(string accountId)
    {
        var trimmed = accountId.Trim();
        return trimmed.Contains("XXX", StringComparison.OrdinalIgnoreCase) ||
               PlaceholderSalesforceAccountIdPattern().IsMatch(trimmed);
    }

    private static bool ContainsHumanReviewSignal(string text) =>
        HumanReviewSignals.Any(signal =>
            text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikePlaceholderUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        return url.Contains("your-", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("your_", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("XXXXX", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("xxxxxxxx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPlaceholderValue(params string?[] values) =>
        values.Any(value => string.IsNullOrWhiteSpace(value) ||
            LooksLikePlaceholderValue(value));

    private static bool LooksLikePlaceholderValue(string value)
    {
        var trimmed = value.Trim();

        if (PlaceholderValues.Any(p =>
                p.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (trimmed.StartsWith("your_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("your-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (RepeatedXPattern().IsMatch(trimmed))
            return true;

        return false;
    }

    private static string? GetInputString(Dictionary<string, object> inputData, string key)
    {
        if (!inputData.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }

    private static AgentResult CreateHumanReviewResult(AgentTask task, string finalAnswer) =>
        new()
        {
            TaskId = task.TaskId,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = AgentStatus.HumanReviewRequired,
            FinalAnswer = finalAnswer,
            AuditTrail =
            [
                new AuditEntry
                {
                    Iteration = 1,
                    Timestamp = DateTime.UtcNow,
                    AgentThought = "Input validation failed before tool execution.",
                    AgentAction = "Reject invalid or unready integration input.",
                    ActionResult = finalAnswer,
                    TokensUsed = "0"
                }
            ],
            TotalIterations = 1
        };

    [GeneratedRegex(@"^01t[a-zA-Z0-9]{12}([a-zA-Z0-9]{3})?$", RegexOptions.Compiled)]
    private static partial Regex SalesforceProductIdPattern();

    [GeneratedRegex(@"^01t([Xx])\1{11,14}$", RegexOptions.Compiled)]
    private static partial Regex PlaceholderSalesforceProductIdPattern();

    [GeneratedRegex(@"^INC\d{7,}$", RegexOptions.Compiled)]
    private static partial Regex ServiceNowIncidentNumberPattern();

    [GeneratedRegex(@"^001[a-zA-Z0-9]{12}([a-zA-Z0-9]{3})?$", RegexOptions.Compiled)]
    private static partial Regex SalesforceAccountIdPattern();

    [GeneratedRegex(@"^001([Xx])\1{11,14}$", RegexOptions.Compiled)]
    private static partial Regex PlaceholderSalesforceAccountIdPattern();

    [GeneratedRegex(@"^\d+$", RegexOptions.Compiled)]
    private static partial Regex HubSpotContactIdPattern();

    [GeneratedRegex(@"^evt_[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex StripeEventIdPattern();

    [GeneratedRegex(@"^cus_[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex StripeCustomerIdPattern();

    [GeneratedRegex(@"^[xX]{4,}$", RegexOptions.Compiled)]
    private static partial Regex RepeatedXPattern();
}