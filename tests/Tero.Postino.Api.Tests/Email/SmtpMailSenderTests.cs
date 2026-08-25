using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tero.Postino.Infrastructure.Configuration;
using Tero.Postino.Infrastructure.Email;

namespace Tero.Postino.Api.Tests.Email;

public sealed class SmtpMailSenderTests : IDisposable
{
    private readonly string _journalPath = Path.Combine(
        Path.GetTempPath(),
        "tero-postino-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendAsync_WhenSmtpIsDisabled_WritesPendingMetadataWithoutConnecting()
    {
        var sender = CreateSender(new SmtpOptions { Enabled = false });

        await sender.SendAsync(
            Guid.NewGuid().ToString(),
            "EmailVerification",
            "en",
            "patient@example.com",
            "Verify email",
            "<p>sensitive body</p>");

        var journalFile = Assert.Single(Directory.GetFiles(_journalPath, "*.txt", SearchOption.AllDirectories));
        var content = await File.ReadAllTextAsync(journalFile);
        Assert.Contains("Estado: pending", content);
        Assert.Contains("FailureCode: smtp_disabled", content);
        Assert.DoesNotContain("sensitive body", content);
    }

    [Fact]
    public async Task SendAsync_WhenSmtpIsEnabledButIncomplete_ThrowsConfigurationError()
    {
        var sender = CreateSender(new SmtpOptions
        {
            Enabled = true,
            Host = "",
            FromAddress = "",
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            Guid.NewGuid().ToString(),
            "EmailVerification",
            "en",
            "patient@example.com",
            "Verify email",
            "<p>body</p>"));

        Assert.Contains("SMTP está habilitado", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_journalPath))
        {
            Directory.Delete(_journalPath, recursive: true);
        }
    }

    private SmtpMailSender CreateSender(SmtpOptions smtpOptions)
    {
        var journal = new MailJournalWriter(
            Options.Create(new MailJournalOptions { BasePath = _journalPath }),
            NullLogger<MailJournalWriter>.Instance);
        return new SmtpMailSender(
            Options.Create(smtpOptions),
            journal,
            NullLogger<SmtpMailSender>.Instance);
    }
}
