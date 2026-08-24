namespace Tero.Postino.Infrastructure.Email;

public sealed class MailJournalOptions
{
    public const string SectionName = "MailJournal";

    /// <summary>Raíz del bind mount (deploy/mails ↔ /mails) — así se ve directo desde el host
    /// sin entrar al contenedor. Configurable por si algún ambiente monta otra ruta.</summary>
    public string BasePath { get; set; } = "/mails";
}
