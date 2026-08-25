using System.Text.Json;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Infrastructure.RabbitMq;

namespace Tero.Postino.Api.Tests.RabbitMq;

public sealed class MailPublisherMetadataTests
{
    [Fact]
    public void MailMessageDto_JsonRoundTripPreservesTemplateModel()
    {
        var message = new MailMessageDto
        {
            To = "patient@example.com",
            CallerClientId = "postino-tests",
            CorrelationId = "correlation-123",
            TemplateModel = new Dictionary<string, object>
            {
                ["contactName"] = "Ana",
                ["durationMinutes"] = 45,
            },
        };

        var serialized = JsonSerializer.SerializeToUtf8Bytes(message);
        var deserialized = JsonSerializer.Deserialize<MailMessageDto>(
            serialized,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(deserialized);
        Assert.Equal("Ana", Assert.IsType<JsonElement>(deserialized.TemplateModel!["contactName"]).GetString());
        Assert.Equal(45, Assert.IsType<JsonElement>(deserialized.TemplateModel["durationMinutes"]).GetInt32());
    }

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
