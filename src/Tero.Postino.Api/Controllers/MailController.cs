using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tero.Contracts.Mail.Requests;
using Tero.Contracts.Claims;
using Tero.Postino.Application.Email;
using Tero.Postino.Application.Email.Ports;
using Tero.ServiceDefaults.CorrelationId;

namespace Tero.Postino.Api.Controllers;

/// <summary>
/// Reemplaza a <c>EmailController</c> (3 endpoints, uno por tipo de notificación) por un
/// único endpoint que recibe cualquier <see cref="MailNotification"/> — el discriminador
/// polimórfico (<c>notificationType</c> en el JSON) resuelve el tipo concreto solo, sin que
/// este controller tenga que saber cuáles existen (input
/// <c>06-boceto-notificaciones-postino-shared</c> del working-task
/// <c>appointments-specialties</c>).
///
/// Sólo microservicios autorizados — <see cref="Authorize"/> exige un JWT válido, y además
/// comprueba a mano el claim <c>client_id</c> (sólo presente en tokens de servicio): un token
/// de usuario final nunca debe poder disparar un envío.
/// </summary>
[ApiController]
[Route("api/mail")]
[Authorize]
public sealed class MailController : ControllerBase
{
    private readonly ISendMailUseCase _sendMailUseCase;
    private readonly CorrelationIdContext _correlationIdContext;

    public MailController(ISendMailUseCase sendMailUseCase, CorrelationIdContext correlationIdContext)
    {
        _sendMailUseCase = sendMailUseCase ?? throw new ArgumentNullException(nameof(sendMailUseCase));
        _correlationIdContext = correlationIdContext ?? throw new ArgumentNullException(nameof(correlationIdContext));
    }

    /// <summary>
    /// Encola un mail a partir de un <see cref="MailNotification"/> tipado — quien lo manda no
    /// sabe (ni le importa) qué plantilla usa Postino ni por qué canal sale.
    /// </summary>
    /// <response code="202">Correo encolado exitosamente para envío</response>
    /// <response code="400">Solicitud inválida</response>
    /// <response code="503">RabbitMQ no está disponible temporalmente</response>
    [HttpPost("send")]
    [ProducesResponseType(typeof(Tero.Contracts.Mail.Responses.MailNotificationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Send([FromBody] MailNotification notification, CancellationToken cancellationToken)
    {
        if (!TryCreateRequestContext(out var requestContext))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Este endpoint requiere identidad de servicio y tenant válidos." });
        }

        var outcome = await _sendMailUseCase.ExecuteAsync(notification, cancellationToken, requestContext).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            var error = new { message = outcome.Message, errors = outcome.Errors };
            return outcome.FailureKind switch
            {
                SendMailFailureKind.Validation => BadRequest(error),
                SendMailFailureKind.Infrastructure => StatusCode(StatusCodes.Status503ServiceUnavailable, error),
                // Un outcome fallido sin categoría es un bug interno; no culpar al caller
                // con un 400 ni sugerir que un retry necesariamente lo resolverá.
                _ => StatusCode(StatusCodes.Status500InternalServerError, error),
            };
        }

        var response = new Tero.Contracts.Mail.Responses.MailNotificationResponse
        {
            MailJobId = outcome.MailJobId,
            Success = true,
            Message = outcome.Message,
        };

        return Accepted(response);
    }

    private bool TryCreateRequestContext(out MailRequestContext requestContext)
    {
        var callerClaim = User.FindFirst(TeroClaimNames.ClientId)?.Value;
        var tenantClaim = User.FindFirst(TeroClaimNames.TenantId)?.Value;
        if (!Guid.TryParse(callerClaim, out var callerId) || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            requestContext = null!;
            return false;
        }

        requestContext = new MailRequestContext
        {
            TenantId = tenantId.ToString("D"),
            CallerClientId = callerId.ToString("D"),
            CorrelationId = NormalizeCorrelationId(_correlationIdContext.GetOrGenerateCorrelationId()),
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };
        return true;
    }

    private static string NormalizeCorrelationId(string value) =>
        value.Length is > 0 and <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? value
            : Guid.NewGuid().ToString("N");
}
