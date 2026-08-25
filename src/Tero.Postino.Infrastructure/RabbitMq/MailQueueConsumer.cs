using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tero.Postino.Infrastructure.Email;
using Tero.Postino.Application.Email.Ports;

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
/// va a <c>postino.mail.dead</c> con metadatos seguros para diagnóstico. La DLQ no duplica
/// destinatarios, cuerpos ni modelos de plantilla que pueden contener credenciales.
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
        await using var channel = await connection.CreateChannelAsync(
                RabbitMqChannelOptions.CreatePublisherConfirmed(),
                stoppingToken)
            .ConfigureAwait(false);

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

        var safeMessageId = MailJournalWriter.NormalizeMessageId(message.MessageId);
        var requestContext = new MailRequestContext
        {
            TenantId = MailJournalWriter.NormalizeIdentifier(message.TenantId),
            CallerClientId = MailJournalWriter.NormalizeIdentifier(message.CallerClientId),
            CorrelationId = MailJournalWriter.NormalizeIdentifier(message.CorrelationId),
            OccurredAtUtc = message.OccurredAtUtc == default ? DateTimeOffset.UtcNow : message.OccurredAtUtc,
        };
        using var auditScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = requestContext.TenantId,
            ["CallerClientId"] = requestContext.CallerClientId,
            ["CorrelationId"] = requestContext.CorrelationId,
            ["NotificationType"] = MailJournalWriter.NormalizeIdentifier(message.TemplateType),
        });

        try
        {
            var htmlBody = !string.IsNullOrEmpty(message.HtmlBody)
                ? message.HtmlBody
                : _templateRenderer.Render(message.TemplateType, message.Language, message.TemplateModel);

            // El asunto viene del archivo .subject.txt del tipo+idioma (BACKLOG.md #1) — antes
            // SendMailUseCase lo mandaba fijo en español dentro del DTO. Un Subject explícito
            // en el mensaje (mails sin plantilla, con HtmlBody propio) sigue teniendo prioridad.
            var subject = !string.IsNullOrEmpty(message.Subject)
                ? message.Subject
                : _templateRenderer.RenderSubject(message.TemplateType, message.Language, message.TemplateModel)
                    ?? "(sin asunto)";

            // Respeta un PlainTextBody explícito; si no vino, se deriva del HTML ya armado
            // (BACKLOG.md #8) — antes el campo existía en el contrato pero nadie lo llenaba.
            var plainTextBody = !string.IsNullOrEmpty(message.PlainTextBody)
                ? message.PlainTextBody
                : MailTemplateRenderer.HtmlToPlainText(htmlBody);

            await _mailSender.SendAsync(
                    safeMessageId,
                    message.TemplateType,
                    message.Language,
                    message.To,
                    subject,
                    htmlBody,
                    plainTextBody,
                    requestContext,
                    stoppingToken)
                .ConfigureAwait(false);
            await channel.BasicAckAsync(args.DeliveryTag, false, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
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
                    safeMessageId, nextAttempt, MaxRetries, backoff);

                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                try
                {
                    await RepublishAsync(channel, ExchangeName, RoutingKey, args.Body, nextAttempt, headers: null, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception publishException) when (!stoppingToken.IsCancellationRequested)
                {
                    await RequeueOriginalAsync(channel, args, safeMessageId, publishException, stoppingToken).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                // Nivel Error + MessageTemplate fijo: queda buscable/alertable en Seq igual que
                // el resto de "Errores en Envío de Email" (ver OBSERVABILITY.md) — el contador
                // de dead-lettered es esa búsqueda, no una métrica nueva.
                _logger.LogError(
                    ex,
                    "Mensaje {MessageId} agotó los {MaxRetries} reintentos — va a dead-letter ({DeadQueue}).",
                    safeMessageId, MaxRetries, DeadQueueName);

                var failureCode = ex.GetType().Name;
                await _journal.WriteAsync(
                        safeMessageId,
                        message.TemplateType,
                        message.Language,
                        message.To,
                        pending: true,
                        failureCode: failureCode,
                        requestContext)
                    .ConfigureAwait(false);

                var deadHeaders = new Dictionary<string, object?>
                {
                    ["x-dead-letter-reason"] = failureCode,
                    ["x-original-routing-key"] = RoutingKey,
                };
                var deadLetterBody = JsonSerializer.SerializeToUtf8Bytes(
                    new DeadLetterMetadata
                    {
                        MessageId = safeMessageId,
                        TemplateType = MailJournalWriter.NormalizeIdentifier(message.TemplateType),
                        Language = MailJournalWriter.NormalizeIdentifier(message.Language),
                        TenantId = requestContext.TenantId,
                        CallerClientId = requestContext.CallerClientId,
                        CorrelationId = requestContext.CorrelationId,
                        OccurredAtUtc = requestContext.OccurredAtUtc,
                        RecipientHash = MailJournalWriter.HashRecipient(message.To),
                        FailureCode = failureCode,
                        FailedAtUtc = DateTime.UtcNow,
                    },
                    SerializerOptions);
                try
                {
                    await RepublishAsync(channel, ExchangeName, DeadRoutingKey, deadLetterBody, retryCount, deadHeaders, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception publishException) when (!stoppingToken.IsCancellationRequested)
                {
                    await RequeueOriginalAsync(channel, args, safeMessageId, publishException, stoppingToken).ConfigureAwait(false);
                    return;
                }
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
        using var publishCancellation = RabbitMqChannelOptions.CreatePublishCancellation(cancellationToken);
        // El canal usa confirms rastreados: el original sólo se ackea después de que este
        // await recibe confirmación positiva. mandatory:true falla si no existe un binding.
        await channel.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: true,
                basicProperties: props,
                body: bytes,
                publishCancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task RequeueOriginalAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        string messageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "No se confirmó la republicación de {MessageId}; el mensaje original vuelve a {Queue}.",
            messageId,
            QueueName);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken)
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
        public string? TenantId { get; set; }
        public string? CallerClientId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public Dictionary<string, JsonElement>? TemplateModel { get; set; }
    }

    private sealed class DeadLetterMetadata
    {
        public string? MessageId { get; set; }
        public string? TemplateType { get; set; }
        public string? Language { get; set; }
        public string? TenantId { get; set; }
        public string CallerClientId { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset OccurredAtUtc { get; set; }
        public string RecipientHash { get; set; } = "";
        public string FailureCode { get; set; } = "";
        public DateTime FailedAtUtc { get; set; }
    }
}
