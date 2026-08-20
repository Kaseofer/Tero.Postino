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
    /// Tipo de plantilla a usar
    /// </summary>
    public string? TemplateType { get; init; }
}
