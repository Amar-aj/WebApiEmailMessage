using Confluent.Kafka;

namespace WebApiEmailMessage.Services;

public class ConsumerService : BackgroundService
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly ILogger<ConsumerService> _logger;

    public ConsumerService(IConfiguration configuration, ILogger<ConsumerService> logger)
    {
        _logger = logger;

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = "MailConsumerGroup",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        _consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _consumer.Close();
    }

    public List<string> ConsumeMessages(string topic, int startIndex, int endIndex, CancellationToken stoppingToken)
    {
        var messages = new List<string>();
        _consumer.Subscribe(SanitizeTopicName(topic));

        try
        {
            int currentIndex = 0;

            while (currentIndex < endIndex && !stoppingToken.IsCancellationRequested)
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (currentIndex >= startIndex && currentIndex < endIndex)
                {
                    messages.Add(consumeResult.Message.Value);
                }
                currentIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            _consumer.Close();
        }

        return messages;
    }
    private string SanitizeTopicName(string topic)
    {
        // Replace invalid characters with an underscore
        return topic.Replace("/", "_").Replace("@", "_").Replace(".", "_");
    }
}
