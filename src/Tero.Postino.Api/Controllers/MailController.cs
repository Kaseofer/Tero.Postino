using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;
using Tero.Postino.Application.Email.Ports;

namespace Tero.Postino.Controllers;

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

    public MailController(ISendMailUseCase sendMailUseCase)
    {
        _sendMailUseCase = sendMailUseCase ?? throw new ArgumentNullException(nameof(sendMailUseCase));
    }

    private bool IsServiceToken() => !string.IsNullOrEmpty(User.FindFirst("client_id")?.Value);

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
        if (!IsServiceToken())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Este endpoint sólo admite tokens de servicio." });
        }

        var outcome = await _sendMailUseCase.ExecuteAsync(notification, cancellationToken).ConfigureAwait(false);

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
}
