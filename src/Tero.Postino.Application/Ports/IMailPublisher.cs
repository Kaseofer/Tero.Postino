using System.Threading;
using System.Threading.Tasks;
using Tero.Messaging.MailContracts;

namespace Tero.Postino.Application.Ports;

public interface IMailPublisher
{
    Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default);
}
