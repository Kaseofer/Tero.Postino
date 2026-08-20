using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Application.Email.UseCases;

/// <summary>
/// Caso de uso para enviar correos de verificación de email
/// </summary>
public sealed class SendVerificationEmailUseCase : ISendVerificationEmailUseCase
{
    private readonly IMailPublisher _mailPublisher;

    public SendVerificationEmailUseCase(IMailPublisher mailPublisher)
    {
        _mailPublisher = mailPublisher ?? throw new ArgumentNullException(nameof(mailPublisher));
    }

    public async Task<SendVerificationEmailOutcome> ExecuteAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        // Validaciones
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return new SendVerificationEmailOutcome
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
            Subject = "Verifica tu correo electrónico",
            TemplateType = "VerifyEmail",
            TemplateModel = new Dictionary<string, object>
            {
                { "userName", request.UserName },
                { "verificationUrl", $"{request.VerificationUrl}?token={request.VerificationToken}" },
                { "priority", request.Priority }
            }
        };

        // Encolar en RabbitMQ
        try
        {
            await _mailPublisher.PublishAsync(mailMessage, cancellationToken);

            return new SendVerificationEmailOutcome
            {
                MailJobId = messageId,
                IsSuccess = true,
                Message = "Correo de verificación encolado exitosamente para envío",
                Errors = []
            };
        }
        catch (Exception ex)
        {
            return new SendVerificationEmailOutcome
            {
                MailJobId = messageId,
                IsSuccess = false,
                Message = $"Error al encolar el correo: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    private static List<string> ValidateRequest(VerifyEmailRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            errors.Add("El correo del destinatario es requerido");

        if (string.IsNullOrWhiteSpace(request.UserName))
            errors.Add("El nombre del usuario es requerido");

        if (string.IsNullOrWhiteSpace(request.VerificationToken))
            errors.Add("El token de verificación es requerido");

        if (string.IsNullOrWhiteSpace(request.VerificationUrl))
            errors.Add("La URL de verificación es requerida");

        // Validar formato de email simple
        if (!string.IsNullOrWhiteSpace(request.RecipientEmail) && !request.RecipientEmail.Contains("@"))
            errors.Add("El formato del correo no es válido");

        return errors;
    }
}
