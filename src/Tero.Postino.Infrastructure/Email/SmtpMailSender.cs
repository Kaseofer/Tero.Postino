using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tero.Postino.Infrastructure.Configuration;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// El envío real por SMTP — mismo mecanismo que <c>Tero.Auth.Api.SmtpEmailSender</c>
/// (<c>System.Net.Mail.SmtpClient</c>, sin dependencia externa), pero acá es el ÚNICO
/// consumidor de <c>postino.mail.queue</c>: si <c>Smtp:Host</c> no está configurado, loguea
/// y no lanza — un mensaje sin SMTP configurado se pierde de la cola iguel (se ackea), no
/// queda reintentando para siempre contra un host que no existe.
/// </summary>
public sealed class SmtpMailSender
{
    private readonly IOptions<SmtpOptions> _options;
    private readonly ILogger<SmtpMailSender> _logger;

    public SmtpMailSender(IOptions<SmtpOptions> options, ILogger<SmtpMailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="plainTextBody"/> es opcional (BACKLOG.md #8) — cuando viene, se manda
    /// como parte alternativa <c>text/plain</c> junto al HTML (multipart/alternative), en vez
    /// de mandar sólo HTML como antes. Sin ella, el comportamiento es el de siempre.
    /// </summary>
    public async Task SendAsync(string to, string subject, string htmlBody, string? plainTextBody = null, CancellationToken cancellationToken = default)
    {
        var smtp = _options.Value;

        if (string.IsNullOrEmpty(smtp.Host) || string.IsNullOrEmpty(smtp.FromAddress))
        {
            _logger.LogWarning(
                "Smtp:Host o Smtp:FromAddress sin configurar — se loguea en vez de mandar. Destinatario: {To}, asunto: {Subject}",
                to,
                subject);
            return;
        }

        using var message = new MailMessage(new MailAddress(smtp.FromAddress, smtp.FromName ?? string.Empty), new MailAddress(to))
        {
            Subject = subject,
        };

        if (!string.IsNullOrEmpty(plainTextBody))
        {
            message.Body = plainTextBody;
            message.IsBodyHtml = false;
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));
        }
        else
        {
            message.Body = htmlBody;
            message.IsBodyHtml = true;
        }

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseSsl,
        };

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Mail enviado a {To}: {Subject}", to, subject);
    }
}
