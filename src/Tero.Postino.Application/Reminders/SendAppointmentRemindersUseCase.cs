using Microsoft.Extensions.Logging;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Reminders.Ports;

namespace Tero.Postino.Application.Reminders;

/// <summary>
/// POST-01: orquesta un tenant por corrida — reclama candidatos (Appointments ya los marcó
/// atómicamente como reclamados) y manda por cada canal que el cliente tenga habilitado.
///
/// Cada envío individual (un email, un WhatsApp) se aísla en su propio try/catch a propósito:
/// el turno YA está marcado como reclamado del lado de Appointments — si un envío falla, no
/// hay forma de "reintentar sólo ese", así que lo único que queda es no dejar que esa falla
/// tire abajo el resto de los candidatos de esta corrida. Queda logueado, no silencioso.
/// </summary>
public sealed class SendAppointmentRemindersUseCase
{
    private readonly IAppointmentsReminderClient _appointments;
    private readonly IWhatsAppGatewayClient _whatsApp;
    private readonly ISendAppointmentNotificationUseCase _email;
    private readonly ILogger<SendAppointmentRemindersUseCase> _logger;

    public SendAppointmentRemindersUseCase(
        IAppointmentsReminderClient appointments,
        IWhatsAppGatewayClient whatsApp,
        ISendAppointmentNotificationUseCase email,
        ILogger<SendAppointmentRemindersUseCase> logger)
    {
        _appointments = appointments;
        _whatsApp = whatsApp;
        _email = email;
        _logger = logger;
    }

    public async Task<int> ExecuteForTenantAsync(Guid tenantId, int windowHours, CancellationToken cancellationToken = default)
    {
        var candidates = await _appointments
            .ClaimPendingRemindersAsync(tenantId, windowHours, cancellationToken)
            .ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            if (candidate.ClientNotifyByEmail && !string.IsNullOrWhiteSpace(candidate.ClientEmail))
            {
                await TrySendEmailAsync(candidate, cancellationToken).ConfigureAwait(false);
            }

            if (candidate.ClientNotifyByWhatsApp && !string.IsNullOrWhiteSpace(candidate.ClientWhatsAppPhone))
            {
                await TrySendWhatsAppAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
            }
        }

        return candidates.Count;
    }

    private async Task TrySendEmailAsync(ReminderCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            var request = new AppointmentNotificationRequest
            {
                RecipientEmail = candidate.ClientEmail!,
                ContactName = candidate.ClientFullName,
                NotificationType = "reminder",
                AppointmentDateTime = candidate.StartsAtUtc,
                ServiceName = candidate.ProfessionalFullName,
            };

            var outcome = await _email.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                _logger.LogWarning(
                    "Recordatorio por email del turno {AppointmentId} no se pudo encolar: {Message}",
                    candidate.AppointmentId,
                    outcome.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el recordatorio por email del turno {AppointmentId}.", candidate.AppointmentId);
        }
    }

    private async Task TrySendWhatsAppAsync(Guid tenantId, ReminderCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            var idempotencyKey = $"reminder:{candidate.AppointmentId:N}";
            var bodyVariables = new[]
            {
                candidate.ClientFullName,
                candidate.ProfessionalFullName,
                candidate.StartsAtUtc.ToString("dd/MM HH:mm"),
            };

            await _whatsApp
                .SendReminderAsync(tenantId, candidate.ClientWhatsAppPhone!, idempotencyKey, bodyVariables, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el recordatorio por WhatsApp del turno {AppointmentId}.", candidate.AppointmentId);
        }
    }
}
