namespace Tero.Postino.Api.Authentication;

/// <summary>
/// Configuración de validación del JWT, bajo la sección <c>Jwt</c>. Este servicio sólo valida
/// (no emite) — mismo criterio que las copias de Appointments/Gateway. Reemplaza el mecanismo
/// anterior (headers estáticos <c>X-Tero-Service-Id</c>/<c>X-Tero-Service-Token</c>, ver
/// README "Autenticación entre servicios" — quedaba marcado ahí mismo como pendiente de
/// unificar con el resto del sistema).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; set; }

    public required string Audience { get; set; }

    /// <summary>Clave simétrica (≥32 bytes) para validar con HS256 — la misma que firma
    /// <c>Tero.Auth.Api</c>. Nunca en <c>appsettings*.json</c> versionado.</summary>
    public required string SigningKey { get; set; }
}
