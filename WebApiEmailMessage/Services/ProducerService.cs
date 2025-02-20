using Confluent.Kafka;

namespace WebApiEmailMessage.Services;

public class ProducerService
{
    private readonly IConfiguration _configuration;
    private readonly IProducer<Null, string> _producer;

    public ProducerService(IConfiguration configuration)
    {
        _configuration = configuration;

        var producerconfig = new ProducerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"]
        };

        _producer = new ProducerBuilder<Null, string>(producerconfig).Build();
    }

    public async Task ProduceAsync(string topic, string message)
    {
        // Sanitize the topic name
        var sanitizedTopic = SanitizeTopicName(topic);

        var kafkamessage = new Message<Null, string> { Value = message };

        await _producer.ProduceAsync(sanitizedTopic, kafkamessage);
    }

    private string SanitizeTopicName(string topic)
    {
        // Replace invalid characters with an underscore
        return topic.Replace("/", "_").Replace("@", "_").Replace(".", "_");
    }
}
