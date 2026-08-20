using Microsoft.Extensions.Options;
using Tero.Postino.Application.Reminders.Ports;
using Tero.Postino.Infrastructure.Auth;
using Tero.Postino.Infrastructure.Configuration;

namespace Tero.Postino.Infrastructure.Reminders;

/// <summary>Cliente HTTP hacia <c>POST {WhatsAppGateway:BaseUrl}api/whatsapp/send</c> (WA-01)
/// — DTO propio, sin referenciar ese servicio.</summary>
public sealed class WhatsAppGatewayClient : AuthenticatedHttpClientBase, IWhatsAppGatewayClient
{
    private readonly IOptions<WhatsAppGatewayOptions> _options;

    public WhatsAppGatewayClient(HttpClient httpClient, IServiceTokenProvider tokenProvider, IOptions<WhatsAppGatewayOptions> options)
        : base(httpClient, tokenProvider)
    {
        _options = options;
    }

    public async Task SendReminderAsync(
        Guid tenantId,
        string to,
        string idempotencyKey,
        IReadOnlyList<string> bodyVariables,
        CancellationToken cancellationToken = default)
    {
        var gatewayOptions = _options.Value;
        var body = new SendWhatsAppMessageWireRequest(to, gatewayOptions.TemplateName, gatewayOptions.LanguageCode, bodyVariables, idempotencyKey);

        using var response = await SendAuthenticatedAsync(
                tenantId,
                () => Post("api/whatsapp/send", body),
                cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private sealed record SendWhatsAppMessageWireRequest(
        string To,
        string TemplateName,
        string LanguageCode,
        IReadOnlyList<string> BodyVariables,
        string IdempotencyKey);
}
