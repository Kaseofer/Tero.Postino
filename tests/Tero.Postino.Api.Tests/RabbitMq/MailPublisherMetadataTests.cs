using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Infrastructure.RabbitMq;

namespace Tero.Postino.Api.Tests.RabbitMq;

public sealed class MailPublisherMetadataTests
{
    [Fact]
    public void CreateBasicProperties_IncludesAuditMetadata()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var message = new MailMessageDto
        {
            MessageId = Guid.NewGuid().ToString("N"),
            To = "patient@example.com",
            TemplateType = "PasswordReset",
            TenantId = Guid.NewGuid().ToString("D"),
            CallerClientId = Guid.NewGuid().ToString("D"),
            CorrelationId = "correlation-123",
            OccurredAtUtc = occurredAt,
        };

        var properties = MailPublisher.CreateBasicProperties(message);

        Assert.True(properties.Persistent);
        Assert.Equal(message.MessageId, properties.MessageId);
        Assert.Equal(message.CorrelationId, properties.CorrelationId);
        Assert.Equal(message.TenantId, properties.Headers!["x-tenant-id"]);
        Assert.Equal(message.CallerClientId, properties.Headers["x-caller-client-id"]);
        Assert.Equal(message.TemplateType, properties.Headers["x-notification-type"]);
        Assert.Equal(occurredAt.ToString("O"), properties.Headers["x-occurred-at-utc"]);
    }
}
