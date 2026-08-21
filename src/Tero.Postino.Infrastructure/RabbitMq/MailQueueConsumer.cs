using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tero.Postino.Infrastructure.Email;

namespace Tero.Postino.Infrastructure.RabbitMq;

/// <summary>
/// El otro extremo de <c>MailPublisher</c> (este servicio) y
/// <c>Tero.Auth.Api.QueueEmailSender</c> — ambos publican al exchange <c>postino.mail</c>,
/// pero con DOS formas de JSON levemente distintas (el de Auth trae <c>templateName</c>/
/// <c>cc</c>/<c>bcc</c>; el propio de Postino trae <c>templateType</c> en vez de
/// <c>templateName</c>, sin esos otros campos). <see cref="MailQueueMessage"/> es el
/// superset de las dos — los campos ausentes en cualquiera de los dos productores quedan
/// simplemente en su default, JSON no exige que coincidan exacto.
///
/// <c>prefetchCount: 1</c> + ack manual, mismo criterio que
/// <c>Tero.WhatsApp.Gateway.InboundWebhookConsumer</c>. A diferencia de aquél, acá NO hay
/// cola de dead-letter todavía: un mensaje que falla se <c>nack(requeue: false)</c> y se
/// pierde, en vez de girar para siempre — aceptable para el volumen y el riesgo de este
/// servicio hoy, pero es la primera limitación real a resolver si el volumen crece.
/// </summary>
public sealed class MailQueueConsumer : BackgroundService
{
    private const string ExchangeName = "postino.mail";
    private const string QueueName = "postino.mail.queue";
    private const string RoutingKey = "mail.send";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConnectionFactory _connectionFactory;
    private readonly SmtpMailSender _mailSender;
    private readonly MailTemplateRenderer _templateRenderer;
    private readonly ILogger<MailQueueConsumer> _logger;

    public MailQueueConsumer(
        ConnectionFactory connectionFactory,
        SmtpMailSender mailSender,
        MailTemplateRenderer templateRenderer,
        ILogger<MailQueueConsumer> logger)
    {
        _connectionFactory = connectionFactory;
        _mailSender = mailSender;
        _templateRenderer = templateRenderer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(stoppingToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: stoppingToken)
            .ConfigureAwait(false);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken)
            .ConfigureAwait(false);
        await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: stoppingToken).ConfigureAwait(false);
        await channel.BasicQosAsync(0, prefetchCount: 1, global: false, stoppingToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => HandleMessageAsync(channel, args, stoppingToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken).ConfigureAwait(false);

        // El consumo real ocurre en ReceivedAsync; esto sólo mantiene vivo el servicio.
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        MailQueueMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<MailQueueMessage>(args.Body.Span, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje de {Queue} no se pudo deserializar — se descarta sin reintentar.", QueueName);
            await channel.BasicNackAsync(args.DeliveryTag, false, requeue: false, stoppingToken).ConfigureAwait(false);
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.To))
        {
            _logger.LogError("Mensaje sin destinatario en {Queue} — se descarta sin reintentar.", QueueName);
            await channel.BasicNackAsync(args.DeliveryTag, false, requeue: false, stoppingToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var htmlBody = !string.IsNullOrEmpty(message.HtmlBody)
                ? message.HtmlBody
                : _templateRenderer.Render(message.TemplateName ?? message.TemplateType, message.Language, message.TemplateModel);

            await _mailSender.SendAsync(message.To, message.Subject ?? "(sin asunto)", htmlBody, stoppingToken).ConfigureAwait(false);
            await channel.BasicAckAsync(args.DeliveryTag, false, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el envío del mensaje {MessageId} — se descarta (nack sin reintentar).", message.MessageId);
            await channel.BasicNackAsync(args.DeliveryTag, false, requeue: false, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Superset de los dos productores — ver el comentario de clase.</summary>
    private sealed class MailQueueMessage
    {
        public string? MessageId { get; set; }
        public string To { get; set; } = "";
        public string? Subject { get; set; }
        public string? HtmlBody { get; set; }
        public string? PlainTextBody { get; set; }
        public string? TemplateName { get; set; }
        public string? TemplateType { get; set; }
        public string? Language { get; set; }
        public Dictionary<string, JsonElement>? TemplateModel { get; set; }
    }
}
