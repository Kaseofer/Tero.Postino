using Tero.Contracts.Mail.Requests;

namespace Tero.Postino.Application.Email.Ports;

/// <summary>
/// Puerto para envío de notificaciones de cita
/// </summary>
public interface ISendAppointmentNotificationUseCase
{
    Task<SendAppointmentNotificationOutcome> ExecuteAsync(AppointmentNotificationRequest request, CancellationToken cancellationToken = default);
}
