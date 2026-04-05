using Xunit;
using FluentAssertions;
using Azentix.Models;

namespace Azentix.Tests.Unit;

[Trait("Category", "Unit")]
public class AgentTaskTests
{
    [Fact]
    public void AgentTask_DefaultTaskId_IsNotEmpty()
    {
        var task = new AgentTask { TaskType = "test", Description = "test" };
        task.TaskId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AgentTask_DefaultPriority_IsNormal()
    {
        var task = new AgentTask { TaskType = "test", Description = "test" };
        task.Priority.Should().Be(TaskPriority.Normal);
    }

    [Fact]
    public void AgentTask_DefaultInputData_IsEmpty()
    {
        var task = new AgentTask { TaskType = "test", Description = "test" };
        task.InputData.Should().BeEmpty();
    }

    [Fact]
    public void AgentResult_Duration_IsNull_WhenNotCompleted()
    {
        var result = new AgentResult { TaskId = "t1", StartedAt = DateTime.UtcNow };
        result.Duration.Should().BeNull();
    }

    [Fact]
    public void AgentResult_Duration_IsCalculated_WhenCompleted()
    {
        var start = DateTime.UtcNow.AddSeconds(-5);
        var result = new AgentResult {
            TaskId = "t1", StartedAt = start, CompletedAt = DateTime.UtcNow };
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void AgentConfiguration_Defaults_AreReasonable()
    {
        var cfg = new AgentConfiguration();
        cfg.MaxIterations.Should().Be(10);
        cfg.TimeoutSeconds.Should().Be(60);
        cfg.ModelDeployment.Should().Be("gpt-4o-mini");
    }

    [Theory]
    [InlineData(TaskPriority.Low, 0)]
    [InlineData(TaskPriority.Normal, 1)]
    [InlineData(TaskPriority.High, 2)]
    [InlineData(TaskPriority.Critical, 3)]
    public void TaskPriority_Values_AreCorrect(TaskPriority priority, int expected)
    {
        ((int)priority).Should().Be(expected);
    }

    [Fact]
    public void AgentResult_DefaultStatus_IsPending()
    {
        var result = new AgentResult { TaskId = "t1" };
        result.Status.Should().Be(AgentStatus.Pending);
    }

    [Fact]
    public void AgentResult_AuditTrail_DefaultIsEmpty()
    {
        var result = new AgentResult { TaskId = "t1" };
        result.AuditTrail.Should().BeEmpty();
    }

    [Fact]
    public void RabbitMQConfiguration_DefaultQueues_AreCorrect()
    {
        var cfg = new RabbitMQConfiguration { AmqpUrl = "amqps://test" };
        cfg.QueueSapPrices.Should().Be("sap-price-changes");
        cfg.QueueIncidents.Should().Be("servicenow-incidents");
        cfg.QueueNotifications.Should().Be("notifications");
    }
}
