namespace Tero.Postino.Application.Reminders.Ports;

public interface IAppointmentsReminderClient
{
    Task<IReadOnlyList<ReminderCandidate>> ClaimPendingRemindersAsync(
        Guid tenantId,
        int windowHours,
        CancellationToken cancellationToken = default);
}
