using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Application.Email.UseCases;

/// <summary>
/// Caso de uso para enviar notificaciones de cita
/// </summary>
public sealed class SendAppointmentNotificationUseCase : ISendAppointmentNotificationUseCase
{
    private readonly IMailPublisher _mailPublisher;

    public SendAppointmentNotificationUseCase(IMailPublisher mailPublisher)
    {
        _mailPublisher = mailPublisher ?? throw new ArgumentNullException(nameof(mailPublisher));
    }

    public async Task<SendAppointmentNotificationOutcome> ExecuteAsync(AppointmentNotificationRequest request, CancellationToken cancellationToken = default)
    {
        // Validaciones
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return new SendAppointmentNotificationOutcome
            {
                MailJobId = string.Empty,
                IsSuccess = false,
                Message = "La solicitud contiene errores de validación",
                Errors = errors
            };
        }

        // Crear mensaje para encolar
        var messageId = Guid.NewGuid().ToString("N");
        var subjectPrefix = request.NotificationType switch
        {
            "confirmation" => "Confirmación",
            "reminder" => "Recordatorio",
            "cancellation" => "Cancelación",
            "rescheduled" => "Reprogramación",
            _ => "Notificación"
        };

        var templateModel = new Dictionary<string, object>
        {
            { "contactName", request.ContactName },
            { "notificationType", request.NotificationType },
            { "serviceName", request.ServiceName },
            { "appointmentDateTime", request.AppointmentDateTime },
            { "priority", request.Priority }
        };

        if (!string.IsNullOrWhiteSpace(request.Location))
            templateModel["location"] = request.Location;

        if (!string.IsNullOrWhiteSpace(request.Description))
            templateModel["description"] = request.Description;

        if (request.DurationMinutes.HasValue)
            templateModel["durationMinutes"] = request.DurationMinutes;

        if (!string.IsNullOrWhiteSpace(request.ContactPhone))
            templateModel["contactPhone"] = request.ContactPhone;

        var mailMessage = new MailMessageDto
        {
            MessageId = messageId,
            To = request.RecipientEmail,
            Subject = $"{subjectPrefix} de cita: {request.ServiceName}",
            TemplateType = "AppointmentNotification",
            TemplateModel = templateModel
        };

        // Encolar en RabbitMQ
        try
        {
            await _mailPublisher.PublishAsync(mailMessage, cancellationToken);

            return new SendAppointmentNotificationOutcome
            {
                MailJobId = messageId,
                IsSuccess = true,
                Message = "Notificación de cita encolada exitosamente para envío",
                Errors = []
            };
        }
        catch (Exception ex)
        {
            return new SendAppointmentNotificationOutcome
            {
                MailJobId = messageId,
                IsSuccess = false,
                Message = $"Error al encolar el correo: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    private static List<string> ValidateRequest(AppointmentNotificationRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            errors.Add("El correo del destinatario es requerido");

        if (string.IsNullOrWhiteSpace(request.ContactName))
            errors.Add("El nombre del contacto es requerido");

        if (string.IsNullOrWhiteSpace(request.NotificationType))
            errors.Add("El tipo de notificación es requerido");

        if (request.AppointmentDateTime == default)
            errors.Add("La fecha y hora de la cita es requerida");

        if (string.IsNullOrWhiteSpace(request.ServiceName))
            errors.Add("El nombre del servicio es requerido");

        if (request.AppointmentDateTime < DateTime.UtcNow && request.NotificationType != "cancellation")
            errors.Add("La fecha de la cita no puede ser en el pasado");

        // Validar formato de email simple
        if (!string.IsNullOrWhiteSpace(request.RecipientEmail) && !request.RecipientEmail.Contains("@"))
            errors.Add("El formato del correo no es válido");

        return errors;
    }
}
