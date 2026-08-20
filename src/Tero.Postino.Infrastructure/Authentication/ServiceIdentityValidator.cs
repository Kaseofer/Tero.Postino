using Microsoft.AspNetCore.Http;
using Tero.Contracts.Claims;
using Tero.ServiceDefaults.Authentication;

namespace Tero.Postino.Infrastructure.Authentication;

/// <summary>
/// Extracts service identity from JWT Bearer token claims.
/// Validates that the request has valid ServiceId and TenantId claims from an issued token.
/// </summary>
public sealed class ServiceIdentityValidator : IServiceIdentityValidator
{
    public Task<ServiceIdentity?> ValidateAsync(HttpContext context)
    {
        var user = context.User;
        if (user == null || !user.Identity?.IsAuthenticated ?? false)
            return Task.FromResult<ServiceIdentity?>(null);

        var serviceIdClaim = user.FindFirst(TeroClaimNames.ClientId)?.Value;
        var tenantIdClaim = user.FindFirst(TeroClaimNames.TenantId)?.Value;

        if (string.IsNullOrEmpty(serviceIdClaim) || !Guid.TryParse(serviceIdClaim, out var serviceId))
            return Task.FromResult<ServiceIdentity?>(null);

        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var tenantId))
            return Task.FromResult<ServiceIdentity?>(null);

        return Task.FromResult<ServiceIdentity?>(new ServiceIdentity(serviceId, tenantId));
    }
}
