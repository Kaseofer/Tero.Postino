using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Bitácora en disco de todos los mails que Postino procesa — pedido explícito para poder
/// revisarlos sin depender de un proveedor SMTP real configurado (hoy no hay ninguno en los
/// ambientes locales/dev). Dos carpetas, mismo contenido por mensaje:
///
/// <list type="bullet">
///   <item><c>{BasePath}/{yyyy}/{MM}/{yyyyMMdd_HHmmss}_{to}.txt</c> — TODO mail que pasó por
///   acá, se haya podido mandar de verdad o no.</item>
///   <item><c>{BasePath}/pendientes/{yyyyMMdd_HHmmss}_{to}.txt</c> — sólo copia de los que NO
///   se mandaron de verdad (sin SMTP configurado, o agotaron reintentos y fueron a
///   dead-letter) — para poder revisarlos/reenviarlos a mano más tarde.</item>
/// </list>
///
/// No lanza si falla la escritura a disco (permisos, mount no montado): un problema de
/// bitácora no puede tirar abajo el envío real de un mail.
/// </summary>
public sealed class MailJournalWriter
{
    private readonly MailJournalOptions _options;
    private readonly ILogger<MailJournalWriter> _logger;

    public MailJournalWriter(IOptions<MailJournalOptions> options, ILogger<MailJournalWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task WriteAsync(string to, string subject, string htmlBody, string? plainTextBody, bool pending, string? pendingReason = null)
    {
        var now = DateTime.UtcNow;
        var fileName = $"{now:yyyyMMdd_HHmmss}_{SanitizeForFileName(to)}.txt";
        var content = BuildContent(to, subject, htmlBody, plainTextBody, now, pendingReason);

        WriteFile(Path.Combine(_options.BasePath, now.ToString("yyyy"), now.ToString("MM")), fileName, content);

        if (pending)
        {
            WriteFile(Path.Combine(_options.BasePath, "pendientes"), fileName, content);
        }

        return Task.CompletedTask;
    }

    private void WriteFile(string directory, string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo escribir la bitácora de mail en {Directory}/{FileName}.", directory, fileName);
        }
    }

    private static string BuildContent(string to, string subject, string htmlBody, string? plainTextBody, DateTime atUtc, string? pendingReason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Fecha (UTC): {atUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Para: {to}");
        sb.AppendLine($"Asunto: {subject}");
        if (pendingReason is not null)
        {
            sb.AppendLine($"Motivo (no enviado): {pendingReason}");
        }
        sb.AppendLine();
        if (!string.IsNullOrEmpty(plainTextBody))
        {
            sb.AppendLine("--- Texto plano ---");
            sb.AppendLine(plainTextBody);
            sb.AppendLine();
        }
        sb.AppendLine("--- HTML ---");
        sb.AppendLine(htmlBody);
        return sb.ToString();
    }

    /// <summary>El email en sí no trae caracteres inválidos en un path, pero por las dudas
    /// (y para Windows, que además prohíbe ':') se reemplaza cualquiera de la lista negra.</summary>
    private static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
