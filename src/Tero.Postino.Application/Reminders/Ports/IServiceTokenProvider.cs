namespace Tero.Postino.Application.Reminders.Ports;

/// <summary>Token de servicio contra <c>Tero.Auth.Api</c>, cacheado por tenant — copia
/// propia del puerto de <c>Tero.WhatsApp.Gateway</c> (mismo contrato, sin referenciarlo).</summary>
public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Descarta el token cacheado de ese tenant — ante un 401, para reintentar una
    /// única vez con uno fresco.</summary>
    void Invalidate(Guid tenantId);
}
