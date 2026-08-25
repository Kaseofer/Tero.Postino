namespace Tero.Postino.Application.Email.Ports;

/// <summary>
/// Contrato para publicar solicitudes de correo en RabbitMQ
/// </summary>
public interface IMailPublisher
{
    Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mensaje de correo para ser encolado
/// </summary>
public sealed record MailMessageDto
{
    /// <summary>
    /// Identificador único del mensaje
    /// </summary>
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Dirección de correo del destinatario
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Asunto del correo
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Cuerpo HTML del correo
    /// </summary>
    public string? HtmlBody { get; init; }

    /// <summary>
    /// Cuerpo de texto plano del correo
    /// </summary>
    public string? PlainTextBody { get; init; }

    /// <summary>
    /// Modelo de datos para renderizar plantilla
    /// </summary>
    public Dictionary<string, object>? TemplateModel { get; init; }

    /// <summary>
    /// Tipo de plantilla a usar — <c>notification.NotificationType.ToString()</c>, ver
    /// <c>SendMailUseCase</c>.
    /// </summary>
    public string? TemplateType { get; init; }

    /// <summary>
    /// Idioma de la organización (<c>es</c>/<c>pt</c>/<c>en</c>) — <c>MailTemplateRenderer</c>
    /// lo usa para elegir la carpeta de plantillas.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Tenant que originó el pedido. Puede ser nulo para procesos internos globales.</summary>
    public string? TenantId { get; init; }

    /// <summary>Cliente de servicio o worker interno que originó el pedido.</summary>
    public required string CallerClientId { get; init; }

    /// <summary>Identificador para seguir la operación entre HTTP, RabbitMQ, journal y DLQ.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Instante en que Postino recibió o generó el pedido.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }
}
