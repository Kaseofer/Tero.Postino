using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tero.Postino.Infrastructure.Email;

namespace Tero.Postino.Infrastructure.RabbitMq;

/// <summary>
/// El otro extremo de <c>MailPublisher</c> — único productor de <c>postino.mail</c> desde que
/// Auth migró de publicar HTML crudo directo a la cola a llamar <c>POST api/mail/send</c>
/// como todos los demás (ver docs/paquetes-shared.md del repo Tero). <see cref="MailQueueMessage"/>
/// ya no necesita tolerar el shape viejo de Auth (<c>templateName</c>/<c>cc</c>/<c>bcc</c>) —
/// espeja 1:1 al <c>MailMessageDto</c> que arma <c>SendMailUseCase</c>.
///
/// <c>prefetchCount: 1</c> + ack manual, mismo criterio que
/// <c>Tero.WhatsApp.Gateway.InboundWebhookConsumer</c>. Un error de render o SMTP ya no se
/// pierde al primer intento (BACKLOG.md #7): se reintenta con backoff creciente vía
/// republish (header <c>x-retry-count</c>) hasta <see cref="MaxRetries"/> veces, y recién ahí
/// va a <c>postino.mail.dead</c> con el motivo y el payload original completo, para
/// reprocesar a mano — ver el query de Seq documentado en OBSERVABILITY.md.
/// </summary>
public sealed class MailQueueConsumer : BackgroundService
{
    private const string ExchangeName = "postino.mail";
    private const string QueueName = "postino.mail.queue";
    private const string RoutingKey = "mail.send";
    private const string DeadQueueName = "postino.mail.dead";
    private const string DeadRoutingKey = "mail.dead";
    private const string RetryCountHeader = "x-retry-count";
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConnectionFactory _connectionFactory;
    private readonly SmtpMailSender _mailSender;
    private readonly MailTemplateRenderer _templateRenderer;
    private readonly MailJournalWriter _journal;
    private readonly ILogger<MailQueueConsumer> _logger;

    public MailQueueConsumer(
        ConnectionFactory connectionFactory,
        SmtpMailSender mailSender,
        MailTemplateRenderer templateRenderer,
        MailJournalWriter journal,
        ILogger<MailQueueConsumer> logger)
    {
        _connectionFactory = connectionFactory;
        _mailSender = mailSender;
        _templateRenderer = templateRenderer;
        _journal = journal;
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

        await channel.QueueDeclareAsync(DeadQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken)
            .ConfigureAwait(false);
        await channel.QueueBindAsync(DeadQueueName, ExchangeName, DeadRoutingKey, cancellationToken: stoppingToken).ConfigureAwait(false);

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

        // Declarados afuera del try: si se agotan los reintentos y el mensaje va a
        // dead-letter, la bitácora de pendientes necesita el asunto/cuerpo ya resueltos —
        // volver a renderizar la plantilla en el catch duplicaría el trabajo (y podría volver
        // a fallar por la misma razón).
        string? subject = null;
        string? htmlBody = null;
        string? plainTextBody = null;

        try
        {
            htmlBody = !string.IsNullOrEmpty(message.HtmlBody)
                ? message.HtmlBody
                : _templateRenderer.Render(message.TemplateType, message.Language, message.TemplateModel);

            // El asunto viene del archivo .subject.txt del tipo+idioma (BACKLOG.md #1) — antes
            // SendMailUseCase lo mandaba fijo en español dentro del DTO. Un Subject explícito
            // en el mensaje (mails sin plantilla, con HtmlBody propio) sigue teniendo prioridad.
            subject = !string.IsNullOrEmpty(message.Subject)
                ? message.Subject
                : _templateRenderer.RenderSubject(message.TemplateType, message.Language, message.TemplateModel)
                    ?? "(sin asunto)";

            // Respeta un PlainTextBody explícito; si no vino, se deriva del HTML ya armado
            // (BACKLOG.md #8) — antes el campo existía en el contrato pero nadie lo llenaba.
            plainTextBody = !string.IsNullOrEmpty(message.PlainTextBody)
                ? message.PlainTextBody
                : MailTemplateRenderer.HtmlToPlainText(htmlBody);

            await _mailSender.SendAsync(message.To, subject, htmlBody, plainTextBody, stoppingToken).ConfigureAwait(false);
            await channel.BasicAckAsync(args.DeliveryTag, false, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var retryCount = GetRetryCount(args.BasicProperties);

            if (retryCount < MaxRetries)
            {
                var nextAttempt = retryCount + 1;
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, nextAttempt));

                _logger.LogWarning(
                    ex,
                    "Falló el envío del mensaje {MessageId} (intento {Attempt}/{MaxRetries}) — reintenta en {Backoff}.",
                    message.MessageId, nextAttempt, MaxRetries, backoff);

                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                await RepublishAsync(channel, ExchangeName, RoutingKey, args.Body, nextAttempt, headers: null, stoppingToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Nivel Error + MessageTemplate fijo: queda buscable/alertable en Seq igual que
                // el resto de "Errores en Envío de Email" (ver OBSERVABILITY.md) — el contador
                // de dead-lettered es esa búsqueda, no una métrica nueva.
                _logger.LogError(
                    ex,
                    "Mensaje {MessageId} agotó los {MaxRetries} reintentos — va a dead-letter ({DeadQueue}).",
                    message.MessageId, MaxRetries, DeadQueueName);

                // Bitácora de pendientes: sólo si llegamos a tener asunto/cuerpo resueltos —
                // si el fallo fue en el render de la plantilla, no hay nada legible para dejar.
                if (subject is not null && htmlBody is not null)
                {
                    await _journal.WriteAsync(message.To, subject, htmlBody, plainTextBody, pending: true, pendingReason: ex.Message)
                        .ConfigureAwait(false);
                }

                var deadHeaders = new Dictionary<string, object?>
                {
                    ["x-dead-letter-reason"] = ex.Message,
                    ["x-original-routing-key"] = RoutingKey,
                };
                await RepublishAsync(channel, ExchangeName, DeadRoutingKey, args.Body, retryCount, deadHeaders, stoppingToken)
                    .ConfigureAwait(false);
            }

            await channel.BasicAckAsync(args.DeliveryTag, false, stoppingToken).ConfigureAwait(false);
        }
    }

    private static async Task RepublishAsync(
        IChannel channel,
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        int retryCount,
        Dictionary<string, object?>? headers,
        CancellationToken cancellationToken)
    {
        var allHeaders = headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(headers);
        allHeaders[RetryCountHeader] = retryCount;

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Headers = allHeaders,
        };

        var bytes = body.ToArray();
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, basicProperties: props, body: bytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not null
            && properties.Headers.TryGetValue(RetryCountHeader, out var raw))
        {
            return raw switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : 0,
                _ => 0,
            };
        }

        return 0;
    }

    /// <summary>Espeja 1:1 al <c>MailMessageDto</c> de <c>SendMailUseCase</c> — ver el
    /// comentario de clase.</summary>
    private sealed class MailQueueMessage
    {
        public string? MessageId { get; set; }
        public string To { get; set; } = "";
        public string? Subject { get; set; }
        public string? HtmlBody { get; set; }
        public string? PlainTextBody { get; set; }
        public string? TemplateType { get; set; }
        public string? Language { get; set; }
        public Dictionary<string, JsonElement>? TemplateModel { get; set; }
    }
}
