using Tero.ServiceDefaults.Authentication;

namespace Tero.Postino.Api.Authentication;

/// <summary>Delega la validación del esquema Bearer en Tero.ServiceDefaults (backlog de
/// Tero.Shared, SH-P1-2) — antes era una copia propia del TokenValidationParameters completo.
/// Este servicio no tiene <c>ITenantContext</c> propio (no persiste nada tenant-scoped
/// todavía), así que no hay nada más que registrar acá.</summary>
public static class TeroJwtAuthenticationExtensions
{
    public static IServiceCollection AddTeroJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
        => services.AddTeroJwtBearerAuthentication<JwtOptions>(configuration);
}
