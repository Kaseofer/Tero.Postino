using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Bitácora de auditoría con metadatos mínimos. No persiste destinatarios, asuntos, cuerpos,
/// modelos de plantilla ni mensajes de excepción porque pueden contener datos personales,
/// códigos de verificación o enlaces de acceso.
/// </summary>
public sealed class MailJournalWriter
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    private readonly MailJournalOptions _options;
    private readonly ILogger<MailJournalWriter> _logger;
    private readonly object _cleanupLock = new();
    private DateTime _nextCleanupAtUtc = DateTime.MinValue;

    public MailJournalWriter(IOptions<MailJournalOptions> options, ILogger<MailJournalWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task WriteAsync(
        string? messageId,
        string? templateType,
        string? language,
        string to,
        bool pending,
        string? failureCode = null)
    {
        var now = DateTime.UtcNow;
        var safeMessageId = NormalizeMessageId(messageId);
        var fileName = $"{now:yyyyMMdd_HHmmss_fff}_{safeMessageId}.txt";
        var content = BuildContent(safeMessageId, templateType, language, to, pending, failureCode, now);

        WriteFile(Path.Combine(_options.BasePath, now.ToString("yyyy"), now.ToString("MM")), fileName, content);
        CleanupExpiredFiles(now);

        return Task.CompletedTask;
    }

    public static string HashRecipient(string recipient)
    {
        var normalized = recipient.Trim().ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string NormalizeMessageId(string? messageId)
    {
        if (Guid.TryParse(messageId, out var id))
        {
            return id.ToString("N");
        }

        return string.IsNullOrWhiteSpace(messageId)
            ? Guid.NewGuid().ToString("N")
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(messageId)))[..32];
    }

    public static string NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
        {
            return "unknown";
        }

        return value;
    }

    private void WriteFile(string directory, string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            SetRestrictedPermissions(directory, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo escribir la bitácora de mail en {Directory}/{FileName}.", directory, fileName);
        }
    }

    private void CleanupExpiredFiles(DateTime nowUtc)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        lock (_cleanupLock)
        {
            if (nowUtc < _nextCleanupAtUtc)
            {
                return;
            }

            _nextCleanupAtUtc = nowUtc.Add(CleanupInterval);
        }

        try
        {
            if (!Directory.Exists(_options.BasePath))
            {
                return;
            }

            var expiresBefore = nowUtc.AddDays(-_options.RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_options.BasePath, "*.txt", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(path) < expiresBefore)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo aplicar la retención de la bitácora de mail en {BasePath}.", _options.BasePath);
        }
    }

    private static string BuildContent(
        string safeMessageId,
        string? templateType,
        string? language,
        string to,
        bool pending,
        string? failureCode,
        DateTime atUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Fecha (UTC): {atUtc:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"MessageId: {safeMessageId}");
        sb.AppendLine($"Tipo: {NormalizeIdentifier(templateType)}");
        sb.AppendLine($"Idioma: {NormalizeIdentifier(language)}");
        sb.AppendLine($"RecipientHash: {HashRecipient(to)}");
        sb.AppendLine($"Estado: {(pending ? "pending" : "sent")}");
        if (!string.IsNullOrWhiteSpace(failureCode))
        {
            sb.AppendLine($"FailureCode: {NormalizeIdentifier(failureCode)}");
        }

        return sb.ToString();
    }

    private static void SetRestrictedPermissions(string directory, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
