using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class RabbitMQPlugin
{
    private readonly RabbitMQConfiguration _cfg;
    private readonly ILogger<RabbitMQPlugin> _log;

    public RabbitMQPlugin(RabbitMQConfiguration cfg, ILogger<RabbitMQPlugin> log)
    { _cfg = cfg; _log = log; }

    [KernelFunction("rabbitmq_publish")]
    [Description("Publish an event to a CloudAMQP RabbitMQ queue. Use for: approval requests, notifications, escalations.")]
    public Task<string> PublishAsync(
        [Description("Queue name: sap-price-changes | servicenow-incidents | stripe-events | approval-queue | notifications")] string queueName,
        [Description("JSON message body")] string messageJson,
        [Description("Message priority 0-9")] byte priority = 0)
    {
        _log.LogInformation("RabbitMQ publish → {Q}", queueName);
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_cfg.AmqpUrl) };
            using var conn    = factory.CreateConnection();
            using var channel = conn.CreateModel();
            channel.QueueDeclare(queueName, durable: true,
                exclusive: false, autoDelete: false);
            var body  = Encoding.UTF8.GetBytes(messageJson);
            var props = channel.CreateBasicProperties();
            props.Persistent   = true;
            props.Priority     = priority;
            props.ContentType  = "application/json";
            props.Timestamp    = new AmqpTimestamp(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            channel.BasicPublish("", queueName, props, body);
            _log.LogInformation("Published {Bytes} bytes → {Q}", body.Length, queueName);
            return Task.FromResult(JsonSerializer.Serialize(new {
                success = true, queue = queueName,
                bytes   = body.Length, at = DateTime.UtcNow }));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RabbitMQ publish failed: {Q}", queueName);
            return Task.FromResult(JsonSerializer.Serialize(new {
                error = ex.Message, queue = queueName }));
        }
    }
}
