namespace Tero.Postino.Application.Reminders.Ports;

public interface IAppointmentsReminderClient
{
    Task<IReadOnlyList<ReminderCandidate>> ClaimPendingRemindersAsync(
        Guid tenantId,
        int windowHours,
        CancellationToken cancellationToken = default);

    Task CompleteReminderClaimAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid claimToken,
        CancellationToken cancellationToken = default);

    Task ReleaseReminderClaimAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid claimToken,
        CancellationToken cancellationToken = default);
}
