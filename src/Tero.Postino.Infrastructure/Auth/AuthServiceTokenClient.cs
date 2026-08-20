using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tero.Postino.Application.Reminders.Ports;
using Tero.Postino.Infrastructure.Configuration;

namespace Tero.Postino.Infrastructure.Auth;

/// <summary>
/// Pide y cachea el token de servicio contra <c>POST {Auth:BaseUrl}api/auth/service-token</c>
/// — copia propia del cliente homónimo de <c>Tero.WhatsApp.Gateway</c> (mismo contrato,
/// misma política de renovación a los 60 segundos de vida restante), sin referenciar ese
/// servicio.
/// </summary>
public sealed class AuthServiceTokenClient : IServiceTokenProvider
{
    private const string ServiceTokenPath = "api/auth/service-token";
    private const int RenewMarginSeconds = 60;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<AuthServiceTokenClient> _logger;
    private readonly ConcurrentDictionary<Guid, CachedToken> _cache = new();

    public AuthServiceTokenClient(HttpClient httpClient, IOptions<AuthOptions> options, ILogger<AuthServiceTokenClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && !NeedsRenewal(cached))
        {
            return cached.AccessToken;
        }

        var fresh = await RequestTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        _cache[tenantId] = fresh;
        return fresh.AccessToken;
    }

    public void Invalidate(Guid tenantId) => _cache.TryRemove(tenantId, out _);

    private static bool NeedsRenewal(CachedToken token) =>
        token.ExpiresAtUtc - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(RenewMarginSeconds);

    private async Task<CachedToken> RequestTokenAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var authOptions = _options.Value;
        var wireRequest = new ServiceTokenWireRequest(authOptions.ClientId, authOptions.ClientSecret, tenantId);

        using var response = await _httpClient
            .PostAsJsonAsync(ServiceTokenPath, wireRequest, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "No se pudo obtener el token de servicio para el tenant {TenantId}: {StatusCode}.",
                tenantId,
                (int)response.StatusCode);

            throw new InvalidOperationException(
                $"No se pudo obtener el token de servicio para el tenant {tenantId} ({(int)response.StatusCode}).");
        }

        var wireResponse = await response.Content
            .ReadFromJsonAsync<ServiceTokenWireResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (wireResponse is null)
        {
            throw new InvalidOperationException("Respuesta vacía de api/auth/service-token.");
        }

        return new CachedToken(wireResponse.AccessToken, wireResponse.ExpiresAtUtc);
    }

    private sealed record ServiceTokenWireRequest(string ClientId, string ClientSecret, Guid TenantId);

    private sealed record ServiceTokenWireResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
