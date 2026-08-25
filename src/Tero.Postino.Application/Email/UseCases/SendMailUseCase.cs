using Microsoft.Extensions.Logging;
using System.Net.Mail;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Application.Email.UseCases;

/// <summary>
/// Reemplaza a los tres casos de uso que existían antes (uno por tipo de notificación) — acá
/// es donde vive todo lo que antes era implícito (plantilla, mapeo de campos), nunca en
/// Tero.Shared (input <c>06-boceto-notificaciones-postino-shared</c> del working-task
/// <c>appointments-specialties</c>).
///
/// El nombre de la plantilla NO se mapea a mano: es directamente <c>notification.NotificationType</c>
/// (ver <see cref="Tero.Postino.Infrastructure.Email.MailTemplateRenderer"/>, que la busca por
/// convención en <c>Templates/{idioma}/{NotificationType}.html</c> y su asunto en
/// <c>Templates/{idioma}/{NotificationType}.subject.txt</c>) — nada que mantener sincronizado
/// cuando se agrega un tipo nuevo, salvo los archivos en sí. El asunto NO se arma acá (ver
/// BACKLOG.md #1): antes quedaba fijo en español sin importar el idioma pedido; ahora lo
/// resuelve el consumidor de la cola con el mismo idioma que el cuerpo.
/// </summary>
public sealed class SendMailUseCase : ISendMailUseCase
{
    private readonly IMailPublisher _mailPublisher;
    private readonly ILogger<SendMailUseCase> _logger;

    public SendMailUseCase(IMailPublisher mailPublisher, ILogger<SendMailUseCase> logger)
    {
        _mailPublisher = mailPublisher ?? throw new ArgumentNullException(nameof(mailPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SendMailOutcome> ExecuteAsync(
        MailNotification notification,
        CancellationToken cancellationToken = default,
        MailRequestContext? requestContext = null)
    {
        var errors = Validate(notification);
        if (errors.Count > 0)
        {
            return new SendMailOutcome
            {
                MailJobId = string.Empty,
                IsSuccess = false,
                Message = "La solicitud contiene errores de validación",
                Errors = errors,
                FailureKind = SendMailFailureKind.Validation,
            };
        }

        var messageId = Guid.NewGuid().ToString("N");
        var auditContext = requestContext ?? new MailRequestContext
        {
            CallerClientId = "postino-internal",
            CorrelationId = messageId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };

        var mailMessage = new MailMessageDto
        {
            MessageId = messageId,
            To = notification.RecipientEmail,
            TemplateType = notification.NotificationType.ToString(),
            Language = notification.LanguageCode,
            TemplateModel = BuildTemplateModel(notification),
            TenantId = auditContext.TenantId,
            CallerClientId = auditContext.CallerClientId,
            CorrelationId = auditContext.CorrelationId,
            OccurredAtUtc = auditContext.OccurredAtUtc,
        };

        try
        {
            await _mailPublisher.PublishAsync(mailMessage, cancellationToken).ConfigureAwait(false);

            return new SendMailOutcome
            {
                MailJobId = messageId,
                IsSuccess = true,
                Message = "Correo encolado exitosamente para envío",
                Errors = [],
                FailureKind = SendMailFailureKind.None,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelar la request no es un fallo de dominio ni de RabbitMQ: se propaga para
            // que ASP.NET finalice la operación con la semántica normal de cancelación.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo encolar el mail {MessageId} de tipo {NotificationType} para el tenant {TenantId}, caller {CallerClientId}, correlación {CorrelationId}.",
                messageId,
                notification.NotificationType,
                auditContext.TenantId,
                auditContext.CallerClientId,
                auditContext.CorrelationId);

            return new SendMailOutcome
            {
                MailJobId = messageId,
                IsSuccess = false,
                // No exponer detalles internos del broker al caller; el diagnóstico completo
                // queda en el log estructurado junto al MessageId.
                Message = "El servicio de correo no está disponible temporalmente",
                Errors = [],
                FailureKind = SendMailFailureKind.Infrastructure,
            };
        }
    }

    /// <summary>
    /// Las 4 variedades de turno comparten un modelo base. Cancelación y reprogramación lo
    /// enriquecen con sus datos propios para que el contrato no pierda información antes de
    /// llegar a la plantilla (PO3-DAT-1).
    /// </summary>
    private static Dictionary<string, object> BuildTemplateModel(MailNotification notification)
    {
        var model = notification switch
        {
            AppointmentNotification n => AppointmentModel(n),

            PasswordResetNotification n => new Dictionary<string, object>
            {
                { "userName", n.RecipientName },
                { "resetUrl", BuildActionUrl(n.ActionUrl, n.Token) },
                { "expirationMinutes", n.ExpirationMinutes },
            },

            EmailVerificationNotification n => new Dictionary<string, object>
            {
                { "userName", n.RecipientName },
                { "verificationUrl", BuildActionUrl(n.ActionUrl, n.Token) },
            },

            AdminCredentialsNotification n => new Dictionary<string, object>
            {
                { "tenantName", n.TenantName },
                { "userEmail", n.RecipientEmail },
                { "setPasswordUrl", BuildActionUrl(n.ActionUrl, n.Token) },
                { "expirationMinutes", n.ExpirationMinutes },
            },

            // C# no puede probar exhaustividad real sobre una jerarquía de clases (no es un
            // union type cerrado) — así que esto SÍ hace falta, a diferencia de un lenguaje
            // con sum types. Tirar acá, en vez de caer a un genérico en silencio, es la
            // garantía que sí podemos dar: un tipo nuevo sin su `case` falla la primera vez
            // que se manda, no se pierde silenciosamente.
            _ => throw new NotSupportedException($"Tipo de notificación no soportado: {notification.GetType().Name}"),
        };

        // Las plantillas visuales comparten datos de marca. Algunos contratos anteriores a
        // 0.9.2 todavía no los transportan; se agregan valores seguros para que el correo no
        // exponga placeholders sin resolver mientras esos productores migran.
        model.TryAdd("organizationName", notification is AdminCredentialsNotification admin
            ? admin.TenantName
            : "Tero");
        model.TryAdd("organizationPhone", string.Empty);
        model.TryAdd("organizationWhatsapp", string.Empty);
        model.TryAdd("organizationEmail", string.Empty);

        return model;
    }

    /// <summary>
    /// <see cref="UriBuilder"/> mantiene el query antes del fragmento. La concatenación
    /// anterior producía <c>#paso?token=...</c> y el servidor nunca recibía el token.
    /// </summary>
    private static string BuildActionUrl(string baseUrl, string token)
    {
        var builder = new UriBuilder(baseUrl);
        var query = builder.Query.TrimStart('?');
        var tokenParameter = $"token={Uri.EscapeDataString(token)}";
        builder.Query = string.IsNullOrEmpty(query) ? tokenParameter : $"{query}&{tokenParameter}";
        return builder.Uri.AbsoluteUri;
    }

    private static Dictionary<string, object> AppointmentModel(AppointmentNotification n)
    {
        var model = new Dictionary<string, object>
        {
            { "contactName", n.RecipientName },
            { "serviceName", n.ServiceName },
            { "appointmentDateTime", n.AppointmentDateTime },
            { "organizationName", ValueOrDefault(n.OrganizationName, "Tero") },
            { "organizationPhone", ValueOrDefault(n.OrganizationPhone) },
            { "organizationWhatsapp", ValueOrDefault(n.OrganizationWhatsApp) },
            // Las plantillas muestran siempre el profesional; los eventos antiguos no lo
            // incluían, por eso el servicio es el fallback más informativo disponible.
            { "professionalName", ValueOrDefault(n.ProfessionalName, n.ServiceName) },
        };

        if (!string.IsNullOrWhiteSpace(n.Location))
        {
            model["location"] = n.Location;
        }

        // Se muestra en el cuerpo (::optional:durationMinutes::) — antes viajaba en el
        // modelo sin que ninguna plantilla lo consumiera (BACKLOG.md #9).
        if (n.DurationMinutes.HasValue)
        {
            model["durationMinutes"] = n.DurationMinutes.Value;
        }

        AddWhenPresent(model, "specialty", n.Specialty);
        AddWhenPresent(model, "appointmentUrl", n.AppointmentUrl);

        if (n is AppointmentCancelledNotification cancelled
            && !string.IsNullOrWhiteSpace(cancelled.CancellationReason))
        {
            model["cancellationReason"] = cancelled.CancellationReason;
        }

        if (n is AppointmentRescheduledNotification rescheduled)
        {
            model["previousAppointmentDateTime"] = rescheduled.PreviousAppointmentDateTime;
        }

        return model;
    }

    private static void AddWhenPresent(Dictionary<string, object> model, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            model[key] = value;
        }
    }

    private static string ValueOrDefault(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static List<string> Validate(MailNotification notification)
    {
        var errors = new List<string>();

        if (!MailAddress.TryCreate(notification.RecipientEmail, out _))
        {
            errors.Add("El formato del correo no es válido");
        }

        if (notification is AccountNotification account)
        {
            if (string.IsNullOrWhiteSpace(account.Token))
            {
                errors.Add("El token de acción es obligatorio");
            }

            if (!Uri.TryCreate(account.ActionUrl, UriKind.Absolute, out var actionUri)
                || (!string.Equals(actionUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(actionUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("La URL de acción debe ser una URL HTTP o HTTPS absoluta");
            }
        }

        // Sólo la cancelación puede avisar de una cita que ya pasó — el resto de las variedades
        // de turno son a futuro por definición.
        if (notification is AppointmentNotification appt
            && notification is not AppointmentCancelledNotification
            && appt.AppointmentDateTime < DateTime.UtcNow)
        {
            errors.Add("La fecha de la cita no puede ser en el pasado");
        }

        if (notification is PasswordResetNotification { ExpirationMinutes: <= 0 }
            or AdminCredentialsNotification { ExpirationMinutes: <= 0 })
        {
            errors.Add("El tiempo de expiración debe ser mayor a 0");
        }

        return errors;
    }
}
