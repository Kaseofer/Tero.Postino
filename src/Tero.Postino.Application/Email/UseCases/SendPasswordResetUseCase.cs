using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Application.Email.UseCases;

/// <summary>
/// Caso de uso para enviar correos de reset de contraseña
/// </summary>
public sealed class SendPasswordResetUseCase : ISendPasswordResetUseCase
{
    private readonly IMailPublisher _mailPublisher;

    public SendPasswordResetUseCase(IMailPublisher mailPublisher)
    {
        _mailPublisher = mailPublisher ?? throw new ArgumentNullException(nameof(mailPublisher));
    }

    public async Task<SendPasswordResetOutcome> ExecuteAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        // Validaciones
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return new SendPasswordResetOutcome
            {
                MailJobId = string.Empty,
                IsSuccess = false,
                Message = "La solicitud contiene errores de validación",
                Errors = errors
            };
        }

        // Crear mensaje para encolar
        var messageId = Guid.NewGuid().ToString("N");
        var mailMessage = new MailMessageDto
        {
            MessageId = messageId,
            To = request.RecipientEmail,
            Subject = "Restablece tu contraseña",
            TemplateType = "ResetPassword",
            TemplateModel = new Dictionary<string, object>
            {
                { "userName", request.UserName },
                { "resetUrl", $"{request.ResetUrl}?token={request.ResetToken}" },
                { "expirationMinutes", request.ExpirationMinutes },
                { "priority", request.Priority }
            }
        };

        // Encolar en RabbitMQ
        try
        {
            await _mailPublisher.PublishAsync(mailMessage, cancellationToken);

            return new SendPasswordResetOutcome
            {
                MailJobId = messageId,
                IsSuccess = true,
                Message = "Correo de reset de contraseña encolado exitosamente para envío",
                Errors = []
            };
        }
        catch (Exception ex)
        {
            return new SendPasswordResetOutcome
            {
                MailJobId = messageId,
                IsSuccess = false,
                Message = $"Error al encolar el correo: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    private static List<string> ValidateRequest(ResetPasswordRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            errors.Add("El correo del destinatario es requerido");

        if (string.IsNullOrWhiteSpace(request.UserName))
            errors.Add("El nombre del usuario es requerido");

        if (string.IsNullOrWhiteSpace(request.ResetToken))
            errors.Add("El token de reset es requerido");

        if (string.IsNullOrWhiteSpace(request.ResetUrl))
            errors.Add("La URL de reset es requerida");

        if (request.ExpirationMinutes <= 0)
            errors.Add("El tiempo de expiración debe ser mayor a 0");

        // Validar formato de email simple
        if (!string.IsNullOrWhiteSpace(request.RecipientEmail) && !request.RecipientEmail.Contains("@"))
            errors.Add("El formato del correo no es válido");

        return errors;
    }
}
