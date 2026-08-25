using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tero.Contracts.Mail.Requests;

namespace Tero.Postino.Infrastructure.Email;

/// <summary>
/// Renderiza el asunto y el cuerpo HTML para los mensajes que llegan a la cola SIN
/// <c>HtmlBody</c>/<c>PlainTextBody</c> — el caso de <c>SendMailUseCase</c>, que arma un
/// <c>TemplateType</c>+<c>Language</c>+<c>TemplateModel</c> y espera que quien consuma la cola
/// sepa transformarlo en un mail real.
///
/// Cuerpo y asunto se buscan por CONVENCIÓN, sin ningún mapeo tipo→archivo que mantener:
/// <c>Templates/{idioma}/{TemplateType}.html</c> y <c>Templates/{idioma}/{TemplateType}.subject.txt</c>,
/// donde <c>TemplateType</c> es directamente <c>MailNotificationType.ToString()</c> (input
/// <c>06-boceto-notificaciones-postino-shared</c> del working-task <c>appointments-specialties</c>).
/// Agregar un tipo de notificación nuevo es agregar los dos archivos — nada de código acá cambia.
///
/// Reemplazo de <c>{{clave}}</c> por texto plano, sin motor de plantillas: alcanza para un
/// archivo, y sumar Handlebars/Scriban sería una dependencia nueva para esto. Un
/// <c>::optional:clave::...::/optional::</c> hace que ese tramo desaparezca entero si
/// <c>clave</c> vino vacía — la única pieza de lógica condicional que soporta. Todo valor
/// sustituido se HTML-encodea (BACKLOG.md #3): el modelo puede traer datos de un tenant
/// (nombre de servicio, ubicación) que no es texto de confianza.
///
/// Idioma sin carpeta propia cae al default (BACKLOG.md #2) en vez de degradar directo al
/// genérico; un <c>languageCode</c> con formato inválido (BACKLOG.md #6) también cae al
/// default antes de tocar el filesystem. Sólo si ni el idioma pedido NI el default tienen el
/// archivo se llega al HTML genérico (arma la lista de pares clave/valor en C#) — para no
/// perder el mensaje ya encolado. Lo que SÍ debe fallar es que falte un archivo al arrancar:
/// ver <see cref="ValidateTemplatesExistOrThrow"/>.
/// </summary>
public sealed class MailTemplateRenderer
{
    private const string DefaultLanguageCode = "es";
    private const string HtmlExtension = "html";
    private const string SubjectExtension = "subject.txt";

    // ISO 639-1 de dos letras, opcionalmente con región ("es" o "es-AR") — alcanza para lo que
    // hoy se siembra (es/pt/en) y cierra la puerta a que un languageCode tipo "../.." termine
    // compuesto en una ruta de archivo (BACKLOG.md #6).
    private static readonly Regex LanguageCodePattern = new(@"^[a-z]{2}(-[A-Z]{2})?$", RegexOptions.Compiled);

    private static readonly Regex OptionalBlockPattern = new(
        @"::optional:(?<key>\w+)::(?<content>.*?)::/optional::",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // El archivo lleva un comentario de documentación arriba, con los nombres de los
    // placeholders escritos en texto plano ("Placeholders: {{contactName}} ..."). Sin este
    // paso, ese texto se sustituye igual que el resto y el comentario —ya no legible como
    // documentación, mezclado con datos reales— viaja dentro del HTML de cada mail enviado.
    private static readonly Regex HtmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    // BACKLOG.md #8: derivar el texto plano del HTML ya renderizado en vez de mantener una
    // plantilla .txt paralela por cada .html — sólo 3 pasos: los links se vuelven "texto (url)",
    // las etiquetas de bloque se vuelven salto de línea, y el resto de las etiquetas desaparece.
    private static readonly Regex AnchorPattern = new(
        @"<a\s+[^>]*href\s*=\s*""(?<url>[^""]*)""[^>]*>(?<text>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BlockTagPattern = new(
        @"</p>|</div>|<br\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnyTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    private readonly string _templatesDirectory;
    private readonly ILogger<MailTemplateRenderer> _logger;

    public MailTemplateRenderer(ILogger<MailTemplateRenderer> logger, string? templatesDirectory = null)
    {
        _logger = logger;
        _templatesDirectory = templatesDirectory ?? Path.Combine(AppContext.BaseDirectory, "Templates");
    }

    public string Render(string? templateType, string? languageCode, Dictionary<string, JsonElement>? model)
    {
        model ??= [];

        var path = ResolveTemplatePath(templateType, languageCode, HtmlExtension);
        if (path is null)
        {
            // El archivo debería viajar con el deploy (ver el csproj) y estar cubierto por
            // ValidateTemplatesExistOrThrow al arrancar — si de todos modos falta acá (ni en
            // el idioma pedido ni en el default), se degrada a genérico en vez de perder el
            // mail ya encolado.
            return RenderGeneric(model);
        }

        return RenderTemplate(File.ReadAllText(path), model);
    }

    /// <summary>
    /// El asunto vive en un archivo aparte (BACKLOG.md #1) — antes lo armaba
    /// <c>SendMailUseCase</c> con texto fijo en español, sin importar el idioma pedido.
    /// <c>null</c> si no hay archivo ni en el idioma pedido ni en el default: el llamador
    /// decide el asunto de emergencia.
    /// </summary>
    public string? RenderSubject(string? templateType, string? languageCode, Dictionary<string, JsonElement>? model)
    {
        model ??= [];

        var path = ResolveTemplatePath(templateType, languageCode, SubjectExtension);
        if (path is null)
        {
            return null;
        }

        // Un archivo de asunto es una sola línea de texto plano, no HTML: ni comentarios ni
        // bloques ::optional:: tienen sentido acá, así que no pasa por RenderTemplate.
        var raw = File.ReadAllText(path).Trim();
        foreach (var (key, value) in model)
        {
            raw = raw.Replace($"{{{{{key}}}}}", value.ToString());
        }

        return raw;
    }

    /// <summary>
    /// Deriva la parte <c>text/plain</c> a partir del HTML ya renderizado — el remitente puede
    /// mandar <c>PlainTextBody</c> explícito, pero cuando no lo hace (el caso de
    /// <c>SendMailUseCase</c>, que sólo arma <c>TemplateModel</c>) esto evita mantener una
    /// plantilla <c>.txt</c> paralela a cada <c>.html</c> (BACKLOG.md #8). Los links
    /// <c>&lt;a href="url"&gt;texto&lt;/a&gt;</c> se vuelven <c>"texto (url)"</c> antes de
    /// perder el resto de las etiquetas, así el destinatario sin HTML igual llega a la URL.
    /// </summary>
    public static string HtmlToPlainText(string html)
    {
        var withLinks = AnchorPattern.Replace(html, m => $"{AnyTagPattern.Replace(m.Groups["text"].Value, string.Empty)} ({m.Groups["url"].Value})");
        var withBreaks = BlockTagPattern.Replace(withLinks, "\n");
        var stripped = AnyTagPattern.Replace(withBreaks, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);

        var lines = decoded
            .Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join("\n\n", lines);
    }

    /// <summary>
    /// Corre al arranque (ver Program.cs): confirma que cada <see cref="MailNotificationType"/>
    /// tenga cuerpo Y asunto en cada carpeta de idioma que YA esté presente bajo
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
                if (!File.Exists(Path.Combine(languageDirectory, $"{notificationType}.{HtmlExtension}")))
                {
                    missing.Add($"{languageCode}/{notificationType}.{HtmlExtension}");
                }

                if (!File.Exists(Path.Combine(languageDirectory, $"{notificationType}.{SubjectExtension}")))
                {
                    missing.Add($"{languageCode}/{notificationType}.{SubjectExtension}");
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Faltan plantillas de mail para {missing.Count} combinación(es) tipo/idioma/archivo: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Resuelve <c>Templates/{idioma}/{templateType}.{extension}</c>. Si el idioma pedido no
    /// tiene ese archivo, intenta primero la variante neutral (<c>pt-BR → pt</c>) y después
    /// el idioma default (<c>es</c>) en vez de irse directo al genérico — BACKLOG.md #2.
    /// <c>null</c> sólo si ni el pedido ni el default tienen el archivo.
    /// </summary>
    private string? ResolveTemplatePath(string? templateType, string? languageCode, string extension)
    {
        if (string.IsNullOrWhiteSpace(templateType))
        {
            return null;
        }

        var requestedLanguage = NormalizeLanguageCode(languageCode);
        var requestedPath = Path.Combine(_templatesDirectory, requestedLanguage, $"{templateType}.{extension}");
        if (File.Exists(requestedPath))
        {
            return requestedPath;
        }

        var neutralLanguage = requestedLanguage.Contains('-')
            ? requestedLanguage.Split('-', 2)[0]
            : null;
        if (neutralLanguage is not null)
        {
            var neutralPath = Path.Combine(_templatesDirectory, neutralLanguage, $"{templateType}.{extension}");
            if (File.Exists(neutralPath))
            {
                _logger.LogInformation(
                    "No hay '{TemplateType}.{Extension}' en idioma '{Language}' — usa la variante neutral '{Neutral}'.",
                    templateType,
                    extension,
                    requestedLanguage,
                    neutralLanguage);
                return neutralPath;
            }
        }

        if (string.Equals(requestedLanguage, DefaultLanguageCode, StringComparison.Ordinal))
        {
            return null;
        }

        _logger.LogWarning(
            "No hay '{TemplateType}.{Extension}' en idioma '{Language}' — cae a '{Default}'.",
            templateType, extension, requestedLanguage, DefaultLanguageCode);

        var fallbackPath = Path.Combine(_templatesDirectory, DefaultLanguageCode, $"{templateType}.{extension}");
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }

    /// <summary>Formato inválido (incluido cualquier intento de path traversal tipo
    /// <c>"../.."</c>, que nunca matchea el regex) cae al default con warning — BACKLOG.md
    /// #6.</summary>
    private string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguageCode;
        }

        var parts = languageCode.Trim().Split('-', 2);
        var normalized = parts.Length == 1
            ? parts[0].ToLowerInvariant()
            : $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";

        if (!LanguageCodePattern.IsMatch(normalized))
        {
            _logger.LogWarning("LanguageCode '{LanguageCode}' con formato inválido — se usa '{Default}'.", languageCode, DefaultLanguageCode);
            return DefaultLanguageCode;
        }

        return normalized;
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
            // HtmlEncode: el modelo puede traer datos de un tenant (nombre de servicio,
            // ubicación) que no es texto de confianza — BACKLOG.md #3.
            withBlocksResolved = withBlocksResolved.Replace($"{{{{{key}}}}}", WebUtility.HtmlEncode(value.ToString()));
        }

        return withBlocksResolved;
    }

    private static string RenderGeneric(Dictionary<string, JsonElement> model)
    {
        var rows = string.Join(
            "",
            model.Select(kv => $"<p><strong>{WebUtility.HtmlEncode(kv.Key)}:</strong> {WebUtility.HtmlEncode(kv.Value.ToString())}</p>"));
        return $"""<div style="font-family: sans-serif;">{rows}</div>""";
    }
}
