using System.Text.Json;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Renderiza HTML para los mensajes que llegan a la cola SIN <c>HtmlBody</c>/<c>PlainTextBody</c>
/// — el caso de <c>SendAppointmentNotificationUseCase</c>, que arma un <c>TemplateType</c> +
/// <c>TemplateModel</c> y espera que quien consuma la cola sepa transformarlo en HTML. Sólo
/// conoce <c>AppointmentNotification</c> hoy — el único tipo que este servicio produce; un
/// tipo desconocido cae a un HTML genérico en vez de fallar, para no perder el mensaje.
/// </summary>
public static class MailTemplateRenderer
{
    public static string Render(string? templateType, Dictionary<string, JsonElement>? model)
    {
        model ??= [];

        return templateType switch
        {
            "AppointmentNotification" => RenderAppointmentNotification(model),
            _ => RenderGeneric(model),
        };
    }

    private static string RenderAppointmentNotification(Dictionary<string, JsonElement> model)
    {
        var contactName = GetString(model, "contactName") ?? "";
        var serviceName = GetString(model, "serviceName") ?? "";
        var appointmentDateTime = GetString(model, "appointmentDateTime") ?? "";
        var location = GetString(model, "location");
        var description = GetString(model, "description");
        var contactPhone = GetString(model, "contactPhone");

        var extra = "";
        if (!string.IsNullOrEmpty(location)) extra += $"<p><strong>Ubicación:</strong> {location}</p>";
        if (!string.IsNullOrEmpty(description)) extra += $"<p>{description}</p>";
        if (!string.IsNullOrEmpty(contactPhone)) extra += $"<p><strong>Contacto:</strong> {contactPhone}</p>";

        return $"""
            <div style="font-family: sans-serif;">
              <p>Hola {contactName},</p>
              <p><strong>{serviceName}</strong> — {appointmentDateTime}</p>
              {extra}
            </div>
            """;
    }

    private static string RenderGeneric(Dictionary<string, JsonElement> model)
    {
        var rows = string.Join("", model.Select(kv => $"<p><strong>{kv.Key}:</strong> {kv.Value}</p>"));
        return $"""<div style="font-family: sans-serif;">{rows}</div>""";
    }

    private static string? GetString(Dictionary<string, JsonElement> model, string key) =>
        model.TryGetValue(key, out var value) ? value.ToString() : null;
}
