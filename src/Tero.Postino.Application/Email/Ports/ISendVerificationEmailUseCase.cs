using Tero.Contracts.Mail.Requests;

namespace Tero.Postino.Application.Email.Ports;

/// <summary>
/// Puerto para envío de correos de verificación
/// </summary>
public interface ISendVerificationEmailUseCase
{
    Task<SendVerificationEmailOutcome> ExecuteAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default);
}
