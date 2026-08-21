using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tero.ServiceDefaults.Authentication;

namespace Tero.Postino.Infrastructure.Auth;

/// <summary>
/// "Pedí el token de servicio del tenant, mandá, y ante un 401 invalidá y reintentá UNA sola
/// vez con uno fresco" — la necesitan tanto <c>AppointmentsReminderClient</c> como
/// <c>WhatsAppGatewayClient</c>; vive acá para no duplicarla. Copiada de la lógica en línea
/// de <c>Tero.WhatsApp.Gateway.Infrastructure.Appointments.AppointmentsClient</c>, extraída
/// a base porque acá hay dos clientes que la necesitan (allá había uno solo).
/// </summary>
public abstract class AuthenticatedHttpClientBase
{
    private readonly HttpClient _httpClient;
    private readonly IServiceTokenProvider _tokenProvider;

    protected AuthenticatedHttpClientBase(HttpClient httpClient, IServiceTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    protected static HttpRequestMessage Post<TBody>(string relativeUrl, TBody body) => new(HttpMethod.Post, relativeUrl)
    {
        Content = JsonContent.Create(body),
    };

    /// <summary><paramref name="requestFactory"/> arma un <see cref="HttpRequestMessage"/>
    /// nuevo por intento: una instancia ya enviada no se puede reenviar.</summary>
    protected async Task<HttpResponseMessage> SendAuthenticatedAsync(
        Guid tenantId,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var response = await SendWithTokenAsync(requestFactory(), token, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        _tokenProvider.Invalidate(tenantId);
        var freshToken = await _tokenProvider.GetAccessTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await SendWithTokenAsync(requestFactory(), freshToken, cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(HttpRequestMessage request, string token, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _httpClient.SendAsync(request, cancellationToken);
    }
}
