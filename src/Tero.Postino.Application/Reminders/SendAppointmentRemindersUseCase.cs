using Microsoft.Extensions.Logging;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Reminders.Ports;

namespace Tero.Postino.Application.Reminders;

/// <summary>
/// POST-01: orquesta un tenant por corrida — reclama candidatos (Appointments ya los marcó
/// atómicamente como reclamados) y manda por cada canal que el cliente tenga habilitado.
///
/// El claim es un lease recuperable: se completa sólo cuando todos los canales solicitados
/// fueron despachados; ante fallo se libera y ante una caída abrupta vence automáticamente.
/// </summary>
public sealed class SendAppointmentRemindersUseCase
{
    private readonly IAppointmentsReminderClient _appointments;
    private readonly IWhatsAppGatewayClient _whatsApp;
    private readonly ISendMailUseCase _email;
    private readonly ILogger<SendAppointmentRemindersUseCase> _logger;

    public SendAppointmentRemindersUseCase(
        IAppointmentsReminderClient appointments,
        IWhatsAppGatewayClient whatsApp,
        ISendMailUseCase email,
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
            try
            {
                await ProcessCandidateAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Best effort: si el host todavía puede llamar a Appointments, libera de
                // inmediato; si no, el lease vence y otra corrida lo recupera.
                await TryReleaseClaimAsync(tenantId, candidate, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        return candidates.Count;
    }

    private async Task ProcessCandidateAsync(
        Guid tenantId,
        ReminderCandidate candidate,
        CancellationToken cancellationToken)
    {
        var allRequestedChannelsSucceeded = true;

        if (candidate.ClientNotifyByEmail)
        {
            if (string.IsNullOrWhiteSpace(candidate.ClientEmail))
            {
                allRequestedChannelsSucceeded = false;
                _logger.LogWarning("El turno {AppointmentId} pide email pero el cliente no tiene dirección.", candidate.AppointmentId);
            }
            else
            {
                allRequestedChannelsSucceeded &= await TrySendEmailAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
            }
        }

        if (candidate.ClientNotifyByWhatsApp)
        {
            if (string.IsNullOrWhiteSpace(candidate.ClientWhatsAppPhone))
            {
                allRequestedChannelsSucceeded = false;
                _logger.LogWarning("El turno {AppointmentId} pide WhatsApp pero el cliente no tiene teléfono.", candidate.AppointmentId);
            }
            else
            {
                allRequestedChannelsSucceeded &= await TrySendWhatsAppAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
            }
        }

        if (allRequestedChannelsSucceeded)
        {
            await TryCompleteClaimAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await TryReleaseClaimAsync(tenantId, candidate, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TrySendEmailAsync(Guid tenantId, ReminderCandidate candidate, CancellationToken cancellationToken)
    {
        try
        {
            var notification = new AppointmentReminderNotification
            {
                RecipientEmail = candidate.ClientEmail!,
                RecipientName = candidate.ClientFullName,
                AppointmentDateTime = candidate.StartsAtUtc,
                ServiceName = candidate.ProfessionalFullName,
            };

            var requestContext = new MailRequestContext
            {
                TenantId = tenantId.ToString("D"),
                CallerClientId = "postino-reminder-worker",
                CorrelationId = $"reminder-{candidate.AppointmentId:N}",
                OccurredAtUtc = DateTimeOffset.UtcNow,
            };
            var outcome = await _email.ExecuteAsync(notification, cancellationToken, requestContext).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                _logger.LogWarning(
                    "Recordatorio por email del turno {AppointmentId} no se pudo encolar: {Message}",
                    candidate.AppointmentId,
                    outcome.Message);
            }

            return outcome.IsSuccess;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el recordatorio por email del turno {AppointmentId}.", candidate.AppointmentId);
            return false;
        }
    }

    private async Task<bool> TrySendWhatsAppAsync(Guid tenantId, ReminderCandidate candidate, CancellationToken cancellationToken)
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

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el recordatorio por WhatsApp del turno {AppointmentId}.", candidate.AppointmentId);
            return false;
        }
    }

    private async Task TryCompleteClaimAsync(
        Guid tenantId,
        ReminderCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            await _appointments
                .CompleteReminderClaimAsync(tenantId, candidate.AppointmentId, candidate.ClaimToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No completar conserva el lease: otra corrida lo recuperará al vencer.
            _logger.LogError(ex, "No se pudo completar el claim del turno {AppointmentId}.", candidate.AppointmentId);
        }
    }

    private async Task TryReleaseClaimAsync(
        Guid tenantId,
        ReminderCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            await _appointments
                .ReleaseReminderClaimAsync(tenantId, candidate.AppointmentId, candidate.ClaimToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Si el release no llega, el lease expira; no se pierde definitivamente.
            _logger.LogError(ex, "No se pudo liberar el claim del turno {AppointmentId}.", candidate.AppointmentId);
        }
    }
}
