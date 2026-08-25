using RabbitMQ.Client;

namespace Tero.Postino.Infrastructure.RabbitMq;

/// <summary>Opciones comunes para canales que publican mensajes durables.</summary>
public static class RabbitMqChannelOptions
{
    public static readonly TimeSpan PublishConfirmationTimeout = TimeSpan.FromSeconds(30);

    public static CreateChannelOptions CreatePublisherConfirmed() => new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    public static CancellationTokenSource CreatePublishCancellation(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishConfirmationTimeout);
        return timeout;
    }
}
