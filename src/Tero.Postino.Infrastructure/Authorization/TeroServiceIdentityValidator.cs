namespace Tero.Postino.Infrastructure.Authorization;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tero.Postino.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementación de validación de identidad para microservicios del ecosistema Tero.
/// Valida mediante tokens API y una lista de servicios autorizados.
/// </summary>
public class TeroServiceIdentityValidator : IServiceIdentityValidator
{
    private readonly ILogger<TeroServiceIdentityValidator> _logger;
    private readonly Dictionary<string, string> _authorizedServices;
    private const string ServiceHeaderName = "X-Tero-Service-Id";
    private const string TokenHeaderName = "X-Tero-Service-Token";

    /// <summary>
    /// Inicializa el validador con la configuración de servicios autorizados.
    /// </summary>
    /// <param name="authorizedServices">Diccionario con ServiceId -> ServiceToken de servicios permitidos</param>
    /// <param name="logger">Logger para auditoría y debugging</param>
    public TeroServiceIdentityValidator(
        Dictionary<string, string> authorizedServices,
        ILogger<TeroServiceIdentityValidator> logger)
    {
        _authorizedServices = authorizedServices ?? new Dictionary<string, string>();
        _logger = logger;

        if (!_authorizedServices.Any())
        {
            _logger.LogWarning("No authorized services configured for Postino. All requests will be rejected.");
        }
    }

    public Task<bool> IsAuthorizedServiceAsync(string serviceId, string token)
    {
        // Validar formato básico
        if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Service ID or token is empty for authorization check");
            return Task.FromResult(false);
        }

        // Validar que el servicio esté registrado
        if (!_authorizedServices.TryGetValue(serviceId, out var expectedToken))
        {
            _logger.LogWarning(
                "Service '{ServiceId}' not found in authorized services list",
                serviceId);
            return Task.FromResult(false);
        }

        // Validar que el token coincida (usar comparison segura contra timing attacks)
        var isValid = CryptographicCompare(token, expectedToken);

        if (!isValid)
        {
            _logger.LogWarning(
                "Invalid token provided for service '{ServiceId}'",
                serviceId);
            return Task.FromResult(false);
        }

        _logger.LogInformation(
            "Service '{ServiceId}' successfully authorized",
            serviceId);

        return Task.FromResult(true);
    }

    public string? GetServiceIdentityFromContext(HttpContext httpContext)
    {
        if (httpContext == null)
            return null;

        // Obtener el Service ID del header
        var serviceId = httpContext.Request.Headers[ServiceHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            _logger.LogDebug("Missing service identity header: {HeaderName}", ServiceHeaderName);
            return null;
        }

        // Validar formato del service ID (solo alphanumericos, guiones y guiones bajos)
        if (!IsValidServiceIdFormat(serviceId))
        {
            _logger.LogWarning("Invalid service ID format: {ServiceId}", serviceId);
            return null;
        }

        return serviceId;
    }

    public bool IsValidTokenFormat(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        // El token debe ser una cadena no vacía, típicamente un UUID o similar
        // Aquí validamos longitud mínima y caracteres permitidos
        // En producción, podría ser un JWT o similar

        // Longitud mínima: 32 caracteres (UUID sin guiones)
        if (token.Length < 32)
            return false;

        // Máximo: 500 caracteres (JWT típico)
        if (token.Length > 500)
            return false;

        // Permitir: alfanuméricos, guiones, puntos (para JWT)
        return token.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_');
    }

    /// <summary>
    /// Obtiene el token de autenticación del header HTTP.
    /// </summary>
    public string? GetTokenFromContext(HttpContext httpContext)
    {
        if (httpContext == null)
            return null;

        var token = httpContext.Request.Headers[TokenHeaderName].ToString();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>
    /// Comparación segura de tokens contra timing attacks.
    /// </summary>
    private static bool CryptographicCompare(string token1, string token2)
    {
        // Usar byte comparison seguro
        var bytes1 = Encoding.UTF8.GetBytes(token1);
        var bytes2 = Encoding.UTF8.GetBytes(token2);

        if (bytes1.Length != bytes2.Length)
            return false;

        int result = 0;
        for (int i = 0; i < bytes1.Length; i++)
        {
            result |= bytes1[i] ^ bytes2[i];
        }

        return result == 0;
    }

    /// <summary>
    /// Valida el formato del identificador del servicio.
    /// Formato esperado: lowercase-alphanumeric-with-hyphens (ej: "auth-api", "appointments-api")
    /// </summary>
    private static bool IsValidServiceIdFormat(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return false;

        // Solo lowercase, números y guiones
        return serviceId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }
}
