using Microsoft.AspNetCore.Http;

namespace Tero.Postino.Application.Authorization;

/// <summary>
/// Contrato para validar la identidad de microservicios del ecosistema Tero.
/// Solo servicios autenticados y autorizados pueden usar Postino.
/// </summary>
public interface IServiceIdentityValidator
{
    /// <summary>
    /// Valida que el cliente sea un microservicio autorizado del ecosistema Tero.
    /// </summary>
    /// <param name="serviceId">Identificador único del servicio (ej: "auth-api", "appointments-api")</param>
    /// <param name="token">Token de servicio para autenticación</param>
    /// <returns>True si es un servicio autorizado, false en caso contrario</returns>
    Task<bool> IsAuthorizedServiceAsync(string serviceId, string token);

    /// <summary>
    /// Obtiene la identidad del servicio desde el contexto de la solicitud HTTP.
    /// </summary>
    /// <param name="httpContext">Contexto HTTP actual</param>
    /// <returns>Identificador del servicio o null si no es válido</returns>
    string? GetServiceIdentityFromContext(HttpContext httpContext);

    /// <summary>
    /// Valida el formato y estructura del token de servicio.
    /// </summary>
    /// <param name="token">Token a validar</param>
    /// <returns>True si el token tiene formato válido</returns>
    bool IsValidTokenFormat(string token);
}
