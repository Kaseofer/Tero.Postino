using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Tero.Postino.Application.Authorization;

namespace Tero.Postino.Filters;

/// <summary>
/// Filtro de autenticación para validar que solo microservicios autorizados usen Postino.
/// Se aplica a través del atributo [TeroServiceAuthentication] en controladores o métodos.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TeroServiceAuthenticationAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var validator = context.HttpContext.RequestServices.GetRequiredService<IServiceIdentityValidator>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<TeroServiceAuthenticationAttribute>>();

        // Obtener identidad del servicio
        var serviceId = validator.GetServiceIdentityFromContext(context.HttpContext);
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            logger.LogWarning("Request missing service identity header (X-Tero-Service-Id)");
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Unauthorized",
                message = "Missing or invalid service identity header",
                header = "X-Tero-Service-Id"
            });
            return;
        }

        // Obtener token - usar casting seguro
        var validatorTyped = validator as dynamic;
        var token = validatorTyped?.GetTokenFromContext(context.HttpContext) as string;

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Request from service '{ServiceId}' missing authentication token", serviceId);
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Unauthorized",
                message = "Missing or invalid service token header",
                header = "X-Tero-Service-Token"
            });
            return;
        }

        // Validar formato del token
        if (!validator.IsValidTokenFormat(token))
        {
            logger.LogWarning("Invalid token format for service '{ServiceId}'", serviceId);
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Unauthorized",
                message = "Invalid token format"
            });
            return;
        }

        // Validar que sea un servicio autorizado
        var isAuthorized = await validator.IsAuthorizedServiceAsync(serviceId, token);
        if (!isAuthorized)
        {
            logger.LogWarning(
                "Unauthorized service attempted access: '{ServiceId}'. Remote IP: {RemoteIp}",
                serviceId,
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Unauthorized",
                message = "Service is not authorized to access Postino API"
            });
            return;
        }

        // Agregar información del servicio al contexto para uso posterior
        context.HttpContext.Items["TeroServiceId"] = serviceId;
        logger.LogInformation("Request authorized from service '{ServiceId}'", serviceId);
    }
}
