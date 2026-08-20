using Tero.Contracts.Mail.Requests;

namespace Tero.Postino.Application.Email.Ports;

/// <summary>
/// Puerto para envío de correos de reset de contraseña
/// </summary>
public interface ISendPasswordResetUseCase
{
    Task<SendPasswordResetOutcome> ExecuteAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
