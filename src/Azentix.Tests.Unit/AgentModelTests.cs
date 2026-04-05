using Xunit;
using FluentAssertions;
using Azentix.Models;

namespace Azentix.Tests.Unit;

[Trait("Category", "Unit")]
public class AgentModelTests
{
    [Fact]
    public void AgentTask_AutoGenerates_TaskId()
    {
        var t = new AgentTask { TaskType = "test", Description = "test" };
        t.TaskId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AgentTask_DefaultPriority_IsNormal()
    {
        var t = new AgentTask { TaskType = "test", Description = "test" };
        t.Priority.Should().Be(TaskPriority.Normal);
    }

    [Fact]
    public void AgentResult_Duration_IsNull_WhenNotCompleted()
    {
        var r = new AgentResult { TaskId = "t1", StartedAt = DateTime.UtcNow };
        r.Duration.Should().BeNull();
    }

    [Fact]
    public void AgentResult_Duration_IsPositive_WhenCompleted()
    {
        var r = new AgentResult {
            TaskId     = "t1",
            StartedAt  = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt= DateTime.UtcNow
        };
        r.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void AgentConfiguration_Defaults_AreReasonable()
    {
        var c = new AgentConfiguration();
        c.MaxIterations.Should().Be(10);
        c.TimeoutSeconds.Should().Be(60);
        c.ModelDeployment.Should().Be("gpt-4o-mini");
    }

    [Theory]
    [InlineData(TaskPriority.Low,      0)]
    [InlineData(TaskPriority.Normal,   1)]
    [InlineData(TaskPriority.High,     2)]
    [InlineData(TaskPriority.Critical, 3)]
    public void TaskPriority_Values_AreOrdered(TaskPriority p, int expected)
        => ((int)p).Should().Be(expected);

    [Fact]
    public void AgentResult_DefaultStatus_IsPending()
    {
        var r = new AgentResult { TaskId = "t1" };
        r.Status.Should().Be(AgentStatus.Pending);
    }

    [Fact]
    public void RabbitMQConfig_DefaultQueues_AreSet()
    {
        var c = new RabbitMQConfiguration { AmqpUrl = "amqps://test" };
        c.QueueSapPrices.Should().Be("sap-price-changes");
        c.QueueNotifications.Should().Be("notifications");
    }

    [Fact]
    public void SapConfiguration_DefaultSalesOrg_IsGB01()
    {
        var c = new SapConfiguration { BaseUrl = "x", ApiKey = "y" };
        c.DefaultSalesOrg.Should().Be("GB01");
        c.System.Should().Be("SANDBOX");
    }

    [Fact]
    public void AuditEntry_CanBeConstructed()
    {
        var e = new AuditEntry {
            Iteration   = 1,
            Timestamp   = DateTime.UtcNow,
            AgentThought = "Thinking...",
            AgentAction  = "sap_get_price(material=MAT-001)"
        };
        e.Iteration.Should().Be(1);
        e.TokensUsed.Should().Be("0");
    }
}
