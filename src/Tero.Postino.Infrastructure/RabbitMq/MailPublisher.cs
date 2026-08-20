using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Infrastructure.RabbitMq;

/// <summary>
/// Implementación de publicador de mensajes de correo en RabbitMQ
/// </summary>
public sealed class MailPublisher : IMailPublisher
{
    private readonly ConnectionFactory _factory;
    private const string ExchangeName = "postino.mail";
    private const string RoutingKey = "mail.send";

    public MailPublisher(ConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            using var conn = _factory.CreateConnection();
            using var ch = conn.CreateModel();

            // Declarar exchange y queue duraderas
            ch.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
            ch.QueueDeclare(queue: "postino.mail.queue", durable: true, exclusive: false, autoDelete: false);
            ch.QueueBind(queue: "postino.mail.queue", exchange: ExchangeName, routingKey: RoutingKey);

            // Serializar el mensaje
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Configurar propiedades del mensaje para persistencia
            var props = ch.CreateBasicProperties();
            props.DeliveryMode = 2; // Persistent
            props.ContentType = "application/json";
            props.MessageId = message.MessageId;

            // Publicar el mensaje
            ch.BasicPublish(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                basicProperties: props,
                body: bytes
            );

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // Log del error (en producción usar ILogger)
            throw new InvalidOperationException($"Error publishing mail message {message.MessageId}", ex);
        }
    }
}
