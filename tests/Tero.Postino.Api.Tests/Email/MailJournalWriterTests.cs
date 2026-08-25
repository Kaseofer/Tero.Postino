using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tero.Postino.Infrastructure.Email;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Api.Tests.Email;

public sealed class MailJournalWriterTests : IDisposable
{
    private readonly string _basePath = Path.Combine(
        Path.GetTempPath(),
        "tero-postino-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAsync_DoesNotPersistRecipientOrSensitiveContent()
    {
        var writer = CreateWriter();
        var messageId = Guid.NewGuid();
        const string recipient = "Patient@example.com";

        await writer.WriteAsync(
            messageId.ToString(),
            "PasswordReset",
            "es",
            recipient,
            pending: false);

        var path = Assert.Single(Directory.GetFiles(_basePath, "*.txt", SearchOption.AllDirectories));
        var content = await File.ReadAllTextAsync(path);

        Assert.Contains(messageId.ToString("N"), Path.GetFileName(path));
        Assert.DoesNotContain(recipient, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(recipient, content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(MailJournalWriter.HashRecipient(recipient), content);
        Assert.Contains("Estado: sent", content);
    }

    [Fact]
    public async Task WriteAsync_WithAuditContext_PersistsSanitizedTraceabilityMetadata()
    {
        var writer = CreateWriter();
        var context = new MailRequestContext
        {
            TenantId = Guid.NewGuid().ToString("D"),
            CallerClientId = Guid.NewGuid().ToString("D"),
            CorrelationId = "correlation-123",
            OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        await writer.WriteAsync(
            Guid.NewGuid().ToString(),
            "PasswordReset",
            "es",
            "patient@example.com",
            pending: false,
            requestContext: context);

        var path = Assert.Single(Directory.GetFiles(_basePath, "*.txt", SearchOption.AllDirectories));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains($"TenantId: {context.TenantId}", content);
        Assert.Contains($"CallerClientId: {context.CallerClientId}", content);
        Assert.Contains($"CorrelationId: {context.CorrelationId}", content);
        Assert.Contains($"OccurredAtUtc: {context.OccurredAtUtc:O}", content);
    }

    [Fact]
    public async Task WriteAsync_UnsafeMetadata_IsNotPersisted()
    {
        var writer = CreateWriter();

        await writer.WriteAsync(
            "token=secret-value",
            "PasswordReset\r\nToken: secret-value",
            "es",
            "patient@example.com",
            pending: true,
            failureCode: "Token=secret-value");

        var path = Assert.Single(Directory.GetFiles(_basePath, "*.txt", SearchOption.AllDirectories));
        var content = await File.ReadAllTextAsync(path);

        Assert.DoesNotContain("secret-value", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tipo: unknown", content);
        Assert.Contains("FailureCode: unknown", content);
    }

    [Fact]
    public async Task WriteAsync_RemovesFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_basePath);
        var expiredPath = Path.Combine(_basePath, "expired.txt");
        await File.WriteAllTextAsync(expiredPath, "old metadata");
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-31));
        var writer = CreateWriter(retentionDays: 30);

        await writer.WriteAsync(Guid.NewGuid().ToString(), "Reminder", "en", "patient@example.com", pending: false);

        Assert.False(File.Exists(expiredPath));
    }

    [Fact]
    public void HashRecipient_NormalizesCaseAndWhitespace()
    {
        var normalized = MailJournalWriter.HashRecipient("patient@example.com");

        Assert.Equal(normalized, MailJournalWriter.HashRecipient("  Patient@Example.COM "));
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    private MailJournalWriter CreateWriter(int retentionDays = 30)
    {
        var options = Options.Create(new MailJournalOptions
        {
            BasePath = _basePath,
            RetentionDays = retentionDays,
        });
        return new MailJournalWriter(options, NullLogger<MailJournalWriter>.Instance);
    }
}
