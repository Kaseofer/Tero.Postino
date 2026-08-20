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

    /// <remarks>
    /// Abre una conexión por mensaje. Una conexión AMQP no es liviana; conviene compartir una
    /// de larga vida cuando el volumen lo justifique, pero ese cambio es de composición.
    /// </remarks>
    public async Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await using var conn = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var ch = await conn.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            // Declarar exchange y queue duraderas
            await ch.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await ch.QueueDeclareAsync(queue: "postino.mail.queue", durable: true, exclusive: false, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await ch.QueueBindAsync(queue: "postino.mail.queue", exchange: ExchangeName, routingKey: RoutingKey,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Serializar el mensaje
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Persistent: el mensaje sobrevive a un reinicio del broker. Sin esto, un mail
            // encolado se pierde antes de entregarse y nadie se entera.
            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = message.MessageId
            };

            await ch.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                mandatory: false,
                basicProperties: props,
                body: bytes,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log del error (en producción usar ILogger)
            throw new InvalidOperationException($"Error publishing mail message {message.MessageId}", ex);
        }
    }
}
