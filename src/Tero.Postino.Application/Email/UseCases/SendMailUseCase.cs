using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Application.Email.UseCases;

/// <summary>
/// Reemplaza a los tres casos de uso que existían antes (uno por tipo de notificación) — acá
/// es donde vive todo lo que antes era implícito (asunto, plantilla, mapeo de campos), nunca
/// en Tero.Shared (input <c>06-boceto-notificaciones-postino-shared</c> del working-task
/// <c>appointments-specialties</c>).
///
/// El nombre de la plantilla NO se mapea a mano: es directamente <c>notification.NotificationType</c>
/// (ver <see cref="MailTemplateRenderer"/>, que la busca por convención en
/// <c>Templates/{idioma}/{NotificationType}.html</c>) — nada que mantener sincronizado cuando
/// se agrega un tipo nuevo, salvo el archivo en sí.
/// </summary>
public sealed class SendMailUseCase : ISendMailUseCase
{
    private readonly IMailPublisher _mailPublisher;

    public SendMailUseCase(IMailPublisher mailPublisher)
    {
        _mailPublisher = mailPublisher ?? throw new ArgumentNullException(nameof(mailPublisher));
    }

    public async Task<SendMailOutcome> ExecuteAsync(MailNotification notification, CancellationToken cancellationToken = default)
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
            };
        }

        var messageId = Guid.NewGuid().ToString("N");
        var (subject, templateModel) = BuildContent(notification);

        var mailMessage = new MailMessageDto
        {
            MessageId = messageId,
            To = notification.RecipientEmail,
            Subject = subject,
            TemplateType = notification.NotificationType.ToString(),
            Language = notification.LanguageCode,
            TemplateModel = templateModel,
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
            };
        }
        catch (Exception ex)
        {
            return new SendMailOutcome
            {
                MailJobId = messageId,
                IsSuccess = false,
                Message = $"Error al encolar el correo: {ex.Message}",
                Errors = [ex.Message],
            };
        }
    }

    /// <summary>
    /// Las 4 variedades de turno (booked/cancelled/rescheduled/reminder) arman el modelo
    /// exactamente igual — sólo cambia el prefijo del asunto, que sale de
    /// <c>NotificationType</c> — así que un único <c>case</c> las cubre a las cuatro
    /// matcheando contra la clase base, en vez de repetir la tupla cuatro veces.
    /// </summary>
    private static (string Subject, Dictionary<string, object> TemplateModel) BuildContent(MailNotification notification) =>
        notification switch
        {
            AppointmentNotification n => ($"{AppointmentSubjectPrefix(n.NotificationType)} de cita: {n.ServiceName}", AppointmentModel(n)),

            PasswordResetNotification n => (
                "Restablece tu contraseña",
                new Dictionary<string, object>
                {
                    { "userName", n.RecipientName },
                    { "resetUrl", $"{n.ActionUrl}?token={n.Token}" },
                    { "expirationMinutes", n.ExpirationMinutes },
                    { "priority", n.Priority.ToString() },
                }),

            EmailVerificationNotification n => (
                "Verifica tu correo electrónico",
                new Dictionary<string, object>
                {
                    { "userName", n.RecipientName },
                    { "verificationUrl", $"{n.ActionUrl}?token={n.Token}" },
                    { "priority", n.Priority.ToString() },
                }),

            AdminCredentialsNotification n => (
                $"Credenciales administrador para {n.TenantName}",
                new Dictionary<string, object>
                {
                    { "tenantName", n.TenantName },
                    { "userEmail", n.RecipientEmail },
                    { "password", n.Password },
                }),

            // C# no puede probar exhaustividad real sobre una jerarquía de clases (no es un
            // union type cerrado) — así que esto SÍ hace falta, a diferencia de un lenguaje
            // con sum types. Tirar acá, en vez de caer a un asunto genérico en silencio como
            // antes, es la garantía que sí podemos dar: un tipo nuevo sin su `case` falla la
            // primera vez que se manda, no se pierde silenciosamente.
            _ => throw new NotSupportedException($"Tipo de notificación no soportado: {notification.GetType().Name}"),
        };

    private static string AppointmentSubjectPrefix(MailNotificationType type) => type switch
    {
        MailNotificationType.AppointmentBooked => "Confirmación",
        MailNotificationType.AppointmentCancelled => "Cancelación",
        MailNotificationType.AppointmentRescheduled => "Reprogramación",
        MailNotificationType.AppointmentReminder => "Recordatorio",
        _ => throw new NotSupportedException($"'{type}' no es un tipo de notificación de turno."),
    };

    private static Dictionary<string, object> AppointmentModel(AppointmentNotification n)
    {
        var model = new Dictionary<string, object>
        {
            { "contactName", n.RecipientName },
            { "serviceName", n.ServiceName },
            { "appointmentDateTime", n.AppointmentDateTime },
            { "priority", n.Priority.ToString() },
        };

        if (!string.IsNullOrWhiteSpace(n.Location))
        {
            model["location"] = n.Location;
        }

        if (n.DurationMinutes.HasValue)
        {
            model["durationMinutes"] = n.DurationMinutes.Value;
        }

        return model;
    }

    private static List<string> Validate(MailNotification notification)
    {
        var errors = new List<string>();

        if (!notification.RecipientEmail.Contains('@'))
        {
            errors.Add("El formato del correo no es válido");
        }

        // Sólo la cancelación puede avisar de una cita que ya pasó — el resto de las variedades
        // de turno son a futuro por definición.
        if (notification is AppointmentNotification appt
            && notification is not AppointmentCancelledNotification
            && appt.AppointmentDateTime < DateTime.UtcNow)
        {
            errors.Add("La fecha de la cita no puede ser en el pasado");
        }

        if (notification is PasswordResetNotification { ExpirationMinutes: <= 0 })
        {
            errors.Add("El tiempo de expiración debe ser mayor a 0");
        }

        return errors;
    }
}
