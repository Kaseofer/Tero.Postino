namespace Tero.Postino.Application.Reminders;

/// <summary>POST-01: copia propia del contrato de Tero.Appointments.Api
/// (<c>ReminderCandidateResponse</c>) — sin referenciar ese servicio, mismo criterio que el
/// resto de los clientes HTTP inter-servicio de Tero.</summary>
public sealed record ReminderCandidate(
    Guid AppointmentId,
    Guid ClaimToken,
    DateTime StartsAtUtc,
    string ProfessionalFullName,
    Guid ClientId,
    string ClientFullName,
    string? ClientEmail,
    string? ClientWhatsAppPhone,
    bool ClientNotifyByEmail,
    bool ClientNotifyByWhatsApp);
