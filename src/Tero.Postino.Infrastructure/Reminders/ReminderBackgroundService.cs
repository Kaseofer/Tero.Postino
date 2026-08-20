using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tero.Postino.Application.Reminders;
using Tero.Postino.Infrastructure.Configuration;

namespace Tero.Postino.Infrastructure.Reminders;

/// <summary>
/// POST-01: dispara <see cref="SendAppointmentRemindersUseCase"/> una vez por tenant
/// configurado, cada <c>Reminders:IntervalMinutes</c>. Un scope nuevo por tenant y por
/// corrida — el use case y sus dependencias son Scoped, este servicio es Singleton (todo
/// <see cref="BackgroundService"/> lo es).
///
/// Una excepción de un tenant no aborta el resto: se loguea y sigue con el siguiente. El
/// timer sigue corriendo aunque una corrida entera falle — no hay reintento inmediato, la
/// próxima oportunidad es la siguiente marca del intervalo.
/// </summary>
public sealed class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ReminderOptions> _options;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReminderOptions> options,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.Value.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var reminderOptions = _options.Value;

        foreach (var tenantId in reminderOptions.TenantIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<SendAppointmentRemindersUseCase>();
                var count = await useCase.ExecuteForTenantAsync(tenantId, reminderOptions.WindowHours, cancellationToken).ConfigureAwait(false);

                if (count > 0)
                {
                    _logger.LogInformation("Recordatorios: {Count} turno(s) procesados para el tenant {TenantId}.", count, tenantId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falló la corrida de recordatorios del tenant {TenantId}.", tenantId);
            }
        }
    }
}
