using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;

namespace Tero.Postino.Application.Email.Ports;

public interface ISendMailUseCase
{
    Task<SendMailOutcome> ExecuteAsync(MailNotification notification, CancellationToken cancellationToken = default);
}
