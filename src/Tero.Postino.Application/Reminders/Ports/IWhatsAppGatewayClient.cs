namespace Tero.Postino.Application.Reminders.Ports;

/// <summary>Puerto hacia WA-01 (<c>Tero.WhatsApp.Gateway</c>). <paramref name="idempotencyKey"/>
/// es responsabilidad de quien llama (ver el comentario en <c>OutboundMessageLog</c> del
/// gateway) — acá se deriva del <c>AppointmentId</c>, así que reenviar el recordatorio del
/// mismo turno dos veces (por ejemplo, tras un reinicio del job a mitad de una corrida) no
/// duplica el mensaje del lado de WhatsApp.</summary>
public interface IWhatsAppGatewayClient
{
    Task SendReminderAsync(
        Guid tenantId,
        string to,
        string idempotencyKey,
        IReadOnlyList<string> bodyVariables,
        CancellationToken cancellationToken = default);
}
