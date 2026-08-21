using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Renderiza HTML para los mensajes que llegan a la cola SIN <c>HtmlBody</c>/<c>PlainTextBody</c>
/// — el caso de <c>SendAppointmentNotificationUseCase</c>, que arma un <c>TemplateType</c> +
/// <c>TemplateModel</c> y espera que quien consuma la cola sepa transformarlo en HTML.
///
/// Las plantillas conocidas viven como archivos <c>.html</c> en <c>Templates/</c> — dirección
/// que ya estaba documentada en el README antes de esta task ("editable con doble clic, sin
/// levantar nada"), no una decisión de esta clase. Reemplazo de <c>{{clave}}</c> por texto
/// plano, sin motor de plantillas: alcanza para un archivo, y sumar Handlebars/Scriban por
/// esto sería una dependencia nueva para un caso de uso. Un <c>::optional:clave::...::/optional::</c>
/// hace que ese tramo desaparezca entero si <c>clave</c> vino vacía — la única pieza de
/// lógica condicional que este renderer soporta.
///
/// Un <c>TemplateType</c> desconocido cae a un HTML genérico (sin archivo, arma la lista de
/// pares clave/valor en C#, porque no hay archivo estático posible para claves que varían) en
/// vez de fallar — para no perder el mensaje.
/// </summary>
public sealed class MailTemplateRenderer
{
    private static readonly Regex OptionalBlockPattern = new(
        @"::optional:(?<key>\w+)::(?<content>.*?)::/optional::",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // El archivo lleva un comentario de documentación arriba, con los nombres de los
    // placeholders escritos en texto plano ("Placeholders: {{contactName}} ..."). Sin este
    // paso, ese texto se sustituye igual que el resto y el comentario —ya no legible como
    // documentación, mezclado con datos reales— viaja dentro del HTML de cada mail enviado.
    private static readonly Regex HtmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly string _templatesDirectory;

    public MailTemplateRenderer(string? templatesDirectory = null)
    {
        _templatesDirectory = templatesDirectory ?? Path.Combine(AppContext.BaseDirectory, "Templates");
    }

    public string Render(string? templateType, Dictionary<string, JsonElement>? model)
    {
        model ??= [];

        var fileName = templateType switch
        {
            "AppointmentNotification" => "appointment-notification.html",
            _ => null,
        };

        if (fileName is null)
        {
            return RenderGeneric(model);
        }

        var path = Path.Combine(_templatesDirectory, fileName);
        if (!File.Exists(path))
        {
            // El archivo debería viajar con el deploy (ver el csproj) — si no está, es un
            // problema de packaging, no de este mensaje puntual. Se degrada a genérico en
            // vez de perder el mail.
            return RenderGeneric(model);
        }

        var template = File.ReadAllText(path);
        return RenderTemplate(template, model);
    }

    private static string RenderTemplate(string template, Dictionary<string, JsonElement> model)
    {
        var withoutComments = HtmlCommentPattern.Replace(template, string.Empty);

        var withBlocksResolved = OptionalBlockPattern.Replace(withoutComments, match =>
        {
            var key = match.Groups["key"].Value;
            var hasValue = model.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.ToString());
            return hasValue ? match.Groups["content"].Value : string.Empty;
        });

        foreach (var (key, value) in model)
        {
            withBlocksResolved = withBlocksResolved.Replace($"{{{{{key}}}}}", value.ToString());
        }

        return withBlocksResolved;
    }

    private static string RenderGeneric(Dictionary<string, JsonElement> model)
    {
        var rows = string.Join("", model.Select(kv => $"<p><strong>{kv.Key}:</strong> {kv.Value}</p>"));
        return $"""<div style="font-family: sans-serif;">{rows}</div>""";
    }
}
