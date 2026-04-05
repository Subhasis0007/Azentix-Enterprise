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
    private readonly RabbitMQConfiguration _config;
    private readonly ILogger<RabbitMQPlugin> _logger;

    public RabbitMQPlugin(RabbitMQConfiguration config, ILogger<RabbitMQPlugin> logger)
    { _config = config; _logger = logger; }

    [KernelFunction("rabbitmq_publish")]
    [Description("Publish an event message to a CloudAMQP RabbitMQ queue. " +
                 "Use for: approval requests, notifications, escalations, sync completions.")]
    public Task<string> PublishAsync(
        [Description("Queue name: sap-price-changes, servicenow-incidents, stripe-events, approval-queue, notifications")] string queueName,
        [Description("JSON message body to publish")] string messageJson,
        [Description("Message priority 0-9")] byte priority = 0)
    {
        _logger.LogInformation("RabbitMQ Publish: {Queue}", queueName);
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_config.AmqpUrl) };
            using var conn = factory.CreateConnection();
            using var channel = conn.CreateModel();
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false,
                autoDelete: false, arguments: null);
            var body = Encoding.UTF8.GetBytes(messageJson);
            var props = channel.CreateBasicProperties();
            props.Persistent = true;
            props.Priority = priority;
            props.ContentType = "application/json";
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            channel.BasicPublish(exchange: "", routingKey: queueName,
                basicProperties: props, body: body);
            _logger.LogInformation("Published to {Queue}: {Size} bytes", queueName, body.Length);
            return Task.FromResult(JsonSerializer.Serialize(new {
                success = true, queue = queueName,
                messageSize = body.Length, publishedAt = DateTime.UtcNow }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ publish failed for {Queue}", queueName);
            return Task.FromResult(JsonSerializer.Serialize(new { error = ex.Message, queueName }));
        }
    }
}
