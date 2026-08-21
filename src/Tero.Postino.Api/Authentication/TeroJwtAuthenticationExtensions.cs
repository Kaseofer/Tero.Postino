using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Tero.Postino.Api.Authentication;

/// <summary>Registra la validación del esquema Bearer — copia del mismo patrón en
/// Auth/Appointments/Gateway. Este servicio no tiene <c>ITenantContext</c> propio (no
/// persiste nada tenant-scoped todavía), así que a diferencia de esas copias no hay nada más
/// que registrar acá.</summary>
public static class TeroJwtAuthenticationExtensions
{
    public static IServiceCollection AddTeroJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;

                bearerOptions.MapInboundClaims = false;

                var signingKeyMaterial = string.IsNullOrEmpty(jwtOptions.SigningKey)
                    ? "unconfigured-signing-key-placeholder-never-matches-a-real-token"
                    : jwtOptions.SigningKey;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKeyMaterial)),
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }
}
