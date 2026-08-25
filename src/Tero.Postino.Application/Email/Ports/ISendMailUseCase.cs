using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;

namespace Tero.Postino.Application.Email.Ports;

public interface ISendMailUseCase
{
    Task<SendMailOutcome> ExecuteAsync(
        MailNotification notification,
        CancellationToken cancellationToken = default,
        MailRequestContext? requestContext = null);
}

public sealed record MailRequestContext
{
    public string? TenantId { get; init; }
    public required string CallerClientId { get; init; }
    public required string CorrelationId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
