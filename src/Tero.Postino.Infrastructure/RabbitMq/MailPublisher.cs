using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Tero.Messaging.MailContracts;

namespace Tero.Postino.Infrastructure.RabbitMq;

public sealed class MailPublisher
{
    private readonly ConnectionFactory _factory;

    public MailPublisher(ConnectionFactory factory)
    {
        _factory = factory;
    }

    public Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        using var ch = conn.CreateModel();
        ch.ExchangeDeclare("postino.mail", ExchangeType.Direct, durable: true);
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var props = ch.CreateBasicProperties();
        props.DeliveryMode = 2; // persistent
        ch.BasicPublish(exchange: "postino.mail", routingKey: "mail.send", basicProperties: props, body: bytes);
        return Task.CompletedTask;
    }
}
