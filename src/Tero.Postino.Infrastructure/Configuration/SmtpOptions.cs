namespace Tero.Postino.Infrastructure.Configuration;

/// <summary>
/// Nombres de clave calcados de la sección <c>Smtp</c> que ya existía en <c>appsettings.json</c>
/// desde antes de esta task (Host/Port/Username/Password/FromAddress/FromName) — no se
/// renombran para no romper la config existente. Nunca en <c>appsettings*.json</c> versionado
/// más allá de un placeholder: llega por user-secrets o variable de entorno.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }
}
