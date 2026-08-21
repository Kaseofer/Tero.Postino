using System.Text.Json;
using System.Text.RegularExpressions;
using Tero.Contracts.Mail.Requests;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Renderiza HTML para los mensajes que llegan a la cola SIN <c>HtmlBody</c>/<c>PlainTextBody</c>
/// — el caso de <c>SendMailUseCase</c>, que arma un <c>TemplateType</c>+<c>Language</c>+
/// <c>TemplateModel</c> y espera que quien consuma la cola sepa transformarlo en HTML.
///
/// La plantilla se busca por CONVENCIÓN, sin ningún mapeo tipo→archivo que mantener:
/// <c>Templates/{idioma}/{TemplateType}.html</c>, donde <c>TemplateType</c> es directamente
/// <c>MailNotificationType.ToString()</c> (input <c>06-boceto-notificaciones-postino-shared</c>
/// del working-task <c>appointments-specialties</c>). Agregar un tipo de notificación nuevo es
/// agregar el archivo — nada de código acá cambia.
///
/// Reemplazo de <c>{{clave}}</c> por texto plano, sin motor de plantillas: alcanza para un
/// archivo, y sumar Handlebars/Scriban sería una dependencia nueva para esto. Un
/// <c>::optional:clave::...::/optional::</c> hace que ese tramo desaparezca entero si
/// <c>clave</c> vino vacía — la única pieza de lógica condicional que soporta.
///
/// Un <c>TemplateType</c>/idioma sin archivo cae a un HTML genérico (arma la lista de pares
/// clave/valor en C#) en vez de fallar — para no perder el mensaje ya encolado. Lo que SÍ debe
/// fallar es que falte un archivo al arrancar: ver <see cref="ValidateTemplatesExistOrThrow"/>.
/// </summary>
public sealed class MailTemplateRenderer
{
    private const string DefaultLanguageCode = "es";

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

    public string Render(string? templateType, string? languageCode, Dictionary<string, JsonElement>? model)
    {
        model ??= [];

        var path = ResolveTemplatePath(templateType, languageCode);
        if (path is null || !File.Exists(path))
        {
            // El archivo debería viajar con el deploy (ver el csproj) y estar cubierto por
            // ValidateTemplatesExistOrThrow al arrancar — si de todos modos falta acá, se
            // degrada a genérico en vez de perder el mail ya encolado.
            return RenderGeneric(model);
        }

        var template = File.ReadAllText(path);
        return RenderTemplate(template, model);
    }

    /// <summary>
    /// Corre al arranque (ver Program.cs): confirma que cada <see cref="MailNotificationType"/>
    /// tenga un archivo de plantilla en cada carpeta de idioma que YA esté presente bajo
    /// <c>Templates/</c> — no exige es/pt/en de antemano, sólo que el idioma que sí se cargó
    /// esté completo. Falla explícito antes de aceptar tráfico, no en el primer envío de un
    /// tipo/idioma sin archivo.
    /// </summary>
    public void ValidateTemplatesExistOrThrow()
    {
        if (!Directory.Exists(_templatesDirectory))
        {
            throw new InvalidOperationException($"No existe el directorio de plantillas: {_templatesDirectory}");
        }

        var languageDirectories = Directory.GetDirectories(_templatesDirectory);
        if (languageDirectories.Length == 0)
        {
            throw new InvalidOperationException($"El directorio de plantillas no tiene ninguna carpeta de idioma: {_templatesDirectory}");
        }

        var missing = new List<string>();
        foreach (var languageDirectory in languageDirectories)
        {
            var languageCode = Path.GetFileName(languageDirectory);
            foreach (var notificationType in Enum.GetNames<MailNotificationType>())
            {
                if (!File.Exists(Path.Combine(languageDirectory, $"{notificationType}.html")))
                {
                    missing.Add($"{languageCode}/{notificationType}.html");
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Faltan plantillas de mail para {missing.Count} combinación(es) tipo/idioma: {string.Join(", ", missing)}");
        }
    }

    private string? ResolveTemplatePath(string? templateType, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(templateType))
        {
            return null;
        }

        var language = string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode;
        return Path.Combine(_templatesDirectory, language, $"{templateType}.html");
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
