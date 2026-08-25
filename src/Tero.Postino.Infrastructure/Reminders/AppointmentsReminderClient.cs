using System.Net.Http.Json;
using Tero.Postino.Application.Reminders;
using Tero.Postino.Application.Reminders.Ports;
using Tero.Postino.Infrastructure.Auth;
using Tero.ServiceDefaults.Authentication;

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
                w.ClaimToken,
                w.StartsAtUtc,
                w.ProfessionalFullName,
                w.ClientId,
                w.ClientFullName,
                w.ClientEmail,
                w.ClientWhatsAppPhone,
                w.ClientNotifyByEmail,
                w.ClientNotifyByWhatsApp,
                w.DurationMinutes is > 0 ? w.DurationMinutes : null,
                w.ServiceName,
                w.Location,
                w.LanguageCode ?? string.Empty,
                string.IsNullOrWhiteSpace(w.TimeZoneId) ? "UTC" : w.TimeZoneId))
            .ToList();
    }

    public Task CompleteReminderClaimAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid claimToken,
        CancellationToken cancellationToken = default) =>
        SetClaimOutcomeAsync(tenantId, appointmentId, claimToken, "complete", cancellationToken);

    public Task ReleaseReminderClaimAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid claimToken,
        CancellationToken cancellationToken = default) =>
        SetClaimOutcomeAsync(tenantId, appointmentId, claimToken, "release", cancellationToken);

    private async Task SetClaimOutcomeAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid claimToken,
        string outcome,
        CancellationToken cancellationToken)
    {
        var body = new ReminderClaimWireRequest(claimToken);
        using var response = await SendAuthenticatedAsync(
                tenantId,
                () => Post($"api/appointments/{appointmentId:D}/reminder-claim/{outcome}", body),
                cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private sealed record ClaimPendingRemindersWireRequest(int WindowHours);

    private sealed record ReminderClaimWireRequest(Guid ClaimToken);

    private sealed record ReminderCandidateWireResponse(
        Guid AppointmentId,
        Guid ClaimToken,
        DateTime StartsAtUtc,
        string ProfessionalFullName,
        Guid ClientId,
        string ClientFullName,
        string? ClientEmail,
        string? ClientWhatsAppPhone,
        bool ClientNotifyByEmail,
        bool ClientNotifyByWhatsApp,
        int? DurationMinutes,
        string? ServiceName,
        string? Location,
        string? LanguageCode,
        string? TimeZoneId);
}
