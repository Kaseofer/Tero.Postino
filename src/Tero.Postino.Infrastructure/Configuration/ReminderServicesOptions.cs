namespace Tero.Postino.Infrastructure.Configuration;

/// <summary>Credenciales contra <c>Tero.Auth.Api</c> para pedir tokens de servicio — mismo
/// rol que la copia de <c>Tero.WhatsApp.Gateway</c>, sin referenciar ese servicio.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public required string BaseUrl { get; set; }

    /// <summary>Coincide con el <c>ClientId</c> que hay que sembrar del lado de Auth para
    /// Postino (hoy sólo existe el seed de <c>whatsapp-gateway</c> — falta agregar éste).</summary>
    public string ClientId { get; set; } = "postino";

    public required string ClientSecret { get; set; }
}

public sealed class AppointmentsOptions
{
    public const string SectionName = "Appointments";

    public required string BaseUrl { get; set; }
}

public sealed class WhatsAppGatewayOptions
{
    public const string SectionName = "WhatsAppGateway";

    public required string BaseUrl { get; set; }

    /// <summary>Nombre de la plantilla aprobada por Meta para el recordatorio — WA-01 sólo
    /// manda plantilla, nunca texto libre fuera de la ventana de 24h de conversación.</summary>
    public string TemplateName { get; set; } = "appointment_reminder";

    public string LanguageCode { get; set; } = "es";
}

/// <summary>POST-01: qué tenants procesa el job y con qué cadencia. Lista de configuración,
/// no consulta a Auth por la lista de organizaciones — mismo criterio que
/// <c>WhatsAppOptions.Numbers</c> en el Gateway (config plana, no service discovery).</summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    public IReadOnlyList<Guid> TenantIds { get; set; } = [];

    /// <summary>Horas hacia adelante desde ahora — un turno entra en la ventana una sola vez
    /// en su vida útil gracias al claim atómico del lado de Appointments, así que agrandar
    /// este valor no genera reenvíos.</summary>
    public int WindowHours { get; set; } = 24;

    public int IntervalMinutes { get; set; } = 15;
}
