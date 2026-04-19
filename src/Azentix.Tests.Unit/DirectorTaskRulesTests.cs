using Azentix.Agents.Director;
using Azentix.Models;
using FluentAssertions;
using Xunit;

namespace Azentix.Tests.Unit;

[Trait("Category", "Unit")]
public class DirectorTaskRulesTests
{
    [Fact]
    public void TryBuildPrevalidatedResult_Flags_Placeholder_Salesforce_Product_Id()
    {
        var task = new AgentTask
        {
            TaskType = "sap-salesforce-price-sync",
            Description = "sync",
            InputData = new Dictionary<string, object>
            {
                ["salesforceProductId"] = "01tXXXXXXXXXXXXXXX"
            }
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, null, null, null, true, out var result);

        handled.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Status.Should().Be(AgentStatus.HumanReviewRequired);
        result.FinalAnswer.Should().Contain("placeholder value");
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Allows_Well_Formed_Product_Id()
    {
        var task = new AgentTask
        {
            TaskType = "sap-salesforce-price-sync",
            Description = "sync",
            InputData = new Dictionary<string, object>
            {
                ["salesforceProductId"] = "01t5e000003K9XAAA0"
            }
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, null, null, null, true, out var result);

        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Flags_ServiceNow_Placeholder_Instance_Url()
    {
        var task = new AgentTask
        {
            TaskType = "servicenow-incident-triage",
            Description = "triage",
            InputData = new Dictionary<string, object>
            {
                ["incidentNumber"] = "INC0001234"
            }
        };

        var configuration = new ServiceNowConfiguration
        {
            InstanceUrl = "https://devXXXXX.service-now.com",
            Username = "admin",
            Password = "your_admin_password"
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, configuration, null, null, true, out var result);

        handled.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Status.Should().Be(AgentStatus.HumanReviewRequired);
        result.FinalAnswer.Should().Contain("ServiceNow instance URL");
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Allows_ServiceNow_Triage_When_Configured()
    {
        var task = new AgentTask
        {
            TaskType = "servicenow-incident-triage",
            Description = "triage",
            InputData = new Dictionary<string, object>
            {
                ["incidentNumber"] = "INC0001234"
            }
        };

        var configuration = new ServiceNowConfiguration
        {
            InstanceUrl = "https://dev12345.service-now.com",
            Username = "admin.user",
            Password = "super-secret"
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, configuration, null, null, true, out var result);

        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Flags_ServiceNow_Triage_When_Rag_Is_Disabled()
    {
        var task = new AgentTask
        {
            TaskType = "servicenow-incident-triage",
            Description = "triage",
            InputData = new Dictionary<string, object>
            {
                ["incidentNumber"] = "INC0001234"
            }
        };

        var configuration = new ServiceNowConfiguration
        {
            InstanceUrl = "https://dev12345.service-now.com",
            Username = "admin.user",
            Password = "super-secret"
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, configuration, null, null, false, out var result);

        handled.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Status.Should().Be(AgentStatus.HumanReviewRequired);
        result.FinalAnswer.Should().Contain("RAG is disabled");
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Does_Not_Flag_NonPlaceholder_Password_Containing_X()
    {
        var task = new AgentTask
        {
            TaskType = "servicenow-incident-triage",
            Description = "triage",
            InputData = new Dictionary<string, object>
            {
                ["incidentNumber"] = "INC0001234"
            }
        };

        var configuration = new ServiceNowConfiguration
        {
            InstanceUrl = "https://dev12345.service-now.com",
            Username = "admin.user",
            Password = "S3cure-xxXx-token"
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, configuration, null, null, true, out var result);

        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Flags_StripeAlert_When_Salesforce_Account_Is_Placeholder()
    {
        var task = new AgentTask
        {
            TaskType = "stripe-billing-alert",
            Description = "stripe",
            InputData = new Dictionary<string, object>
            {
                ["stripeEventType"] = "payment_intent.payment_failed",
                ["stripeEventId"] = "evt_test_1234567890",
                ["stripeCustomerId"] = "cus_test_123",
                ["salesforceAccountId"] = "001XXXXXXXXXXXXXXX",
                ["hubspotContactId"] = "123456"
            }
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, null, null, null, true, out var result);

        handled.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Status.Should().Be(AgentStatus.HumanReviewRequired);
        result.FinalAnswer.Should().Contain("salesforceAccountId");
        result.FinalAnswer.Should().Contain("placeholder");
    }

    [Fact]
    public void TryBuildPrevalidatedResult_Allows_StripeAlert_With_Valid_Input()
    {
        var task = new AgentTask
        {
            TaskType = "stripe-billing-alert",
            Description = "stripe",
            InputData = new Dictionary<string, object>
            {
                ["stripeEventType"] = "payment_intent.payment_failed",
                ["stripeEventId"] = "evt_test_1234567890",
                ["stripeCustomerId"] = "cus_test_123",
                ["salesforceAccountId"] = "0015e00000ABCDEAA4",
                ["hubspotContactId"] = "123456"
            }
        };

        var handled = DirectorTaskRules.TryBuildPrevalidatedResult(task, null, null, null, null, true, out var result);

        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void InferFinalStatus_Returns_HumanReviewRequired_When_Final_Answer_Requests_Manual_Review()
    {
        var status = DirectorTaskRules.InferFinalStatus(
            "Final Answer: The task is marked for human review due to missing product information.",
            "The task is marked for human review due to missing product information.");

        status.Should().Be(AgentStatus.HumanReviewRequired);
    }

    [Fact]
    public void InferFinalStatus_Returns_Completed_For_Normal_Final_Answer()
    {
        var status = DirectorTaskRules.InferFinalStatus(
            "Final Answer: Salesforce pricebook updated successfully.",
            "Salesforce pricebook updated successfully.");

        status.Should().Be(AgentStatus.Completed);
    }
}