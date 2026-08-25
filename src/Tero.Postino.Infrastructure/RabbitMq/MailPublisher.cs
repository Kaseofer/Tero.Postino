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
            await using var ch = await conn.CreateChannelAsync(
                    RabbitMqChannelOptions.CreatePublisherConfirmed(),
                    cancellationToken)
                .ConfigureAwait(false);

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
            var props = CreateBasicProperties(message);

            // Con confirms rastreados, este await sólo completa después del ack del broker.
            // mandatory:true convierte también un mensaje no enrutable en PublishException.
            using var publishCancellation = RabbitMqChannelOptions.CreatePublishCancellation(cancellationToken);
            await ch.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                mandatory: true,
                basicProperties: props,
                body: bytes,
                cancellationToken: publishCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Log del error (en producción usar ILogger)
            throw new InvalidOperationException($"Error publishing mail message {message.MessageId}", ex);
        }
    }

    public static BasicProperties CreateBasicProperties(MailMessageDto message) => new()
    {
        Persistent = true,
        ContentType = "application/json",
        MessageId = message.MessageId,
        CorrelationId = message.CorrelationId,
        Headers = new Dictionary<string, object?>
        {
            ["x-tenant-id"] = message.TenantId ?? "unknown",
            ["x-caller-client-id"] = message.CallerClientId,
            ["x-notification-type"] = message.TemplateType ?? "unknown",
            ["x-occurred-at-utc"] = message.OccurredAtUtc.ToString("O"),
        },
    };
}
