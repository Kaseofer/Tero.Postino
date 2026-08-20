using System.Net.Http.Json;
using Tero.Postino.Application.Reminders;
using Tero.Postino.Application.Reminders.Ports;
using Tero.Postino.Infrastructure.Auth;

namespace Tero.Postino.Infrastructure.Reminders;

/// <summary>Cliente HTTP hacia <c>POST {Appointments:BaseUrl}api/appointments/claim-pending-reminders</c>
/// — DTOs propios, sin referenciar ese servicio.</summary>
public sealed class AppointmentsReminderClient : AuthenticatedHttpClientBase, IAppointmentsReminderClient
{
    public AppointmentsReminderClient(HttpClient httpClient, IServiceTokenProvider tokenProvider)
        : base(httpClient, tokenProvider)
    {
    }

    public async Task<IReadOnlyList<ReminderCandidate>> ClaimPendingRemindersAsync(
        Guid tenantId,
        int windowHours,
        CancellationToken cancellationToken = default)
    {
        var body = new ClaimPendingRemindersWireRequest(windowHours);

        using var response = await SendAuthenticatedAsync(
                tenantId,
                () => Post("api/appointments/claim-pending-reminders", body),
                cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var wire = await response.Content
            .ReadFromJsonAsync<List<ReminderCandidateWireResponse>>(cancellationToken)
            .ConfigureAwait(false);

        return (wire ?? [])
            .Select(w => new ReminderCandidate(
                w.AppointmentId,
                w.StartsAtUtc,
                w.ProfessionalFullName,
                w.ClientId,
                w.ClientFullName,
                w.ClientEmail,
                w.ClientWhatsAppPhone,
                w.ClientNotifyByEmail,
                w.ClientNotifyByWhatsApp))
            .ToList();
    }

    private sealed record ClaimPendingRemindersWireRequest(int WindowHours);

    private sealed record ReminderCandidateWireResponse(
        Guid AppointmentId,
        DateTime StartsAtUtc,
        string ProfessionalFullName,
        Guid ClientId,
        string ClientFullName,
        string? ClientEmail,
        string? ClientWhatsAppPhone,
        bool ClientNotifyByEmail,
        bool ClientNotifyByWhatsApp);
}
