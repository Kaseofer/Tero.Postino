using Microsoft.AspNetCore.Mvc;
using Tero.Contracts.Mail.Requests;
using Tero.Contracts.Mail.Responses;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Filters;

namespace Tero.Postino.Controllers;

/// <summary>
/// Controlador para endpoints de envío de correos.
/// Solo microservicios autorizados del ecosistema Tero pueden usar estos endpoints.
/// Se requieren headers: X-Tero-Service-Id y X-Tero-Service-Token
/// </summary>
[ApiController]
[Route("api/[controller]")]
[TeroServiceAuthentication]
public sealed class EmailController : ControllerBase
{
    private readonly ISendVerificationEmailUseCase _sendVerificationEmailUseCase;
    private readonly ISendPasswordResetUseCase _sendPasswordResetUseCase;
    private readonly ISendAppointmentNotificationUseCase _sendAppointmentNotificationUseCase;

    public EmailController(
        ISendVerificationEmailUseCase sendVerificationEmailUseCase,
        ISendPasswordResetUseCase sendPasswordResetUseCase,
        ISendAppointmentNotificationUseCase sendAppointmentNotificationUseCase)
    {
        _sendVerificationEmailUseCase = sendVerificationEmailUseCase ?? throw new ArgumentNullException(nameof(sendVerificationEmailUseCase));
        _sendPasswordResetUseCase = sendPasswordResetUseCase ?? throw new ArgumentNullException(nameof(sendPasswordResetUseCase));
        _sendAppointmentNotificationUseCase = sendAppointmentNotificationUseCase ?? throw new ArgumentNullException(nameof(sendAppointmentNotificationUseCase));
    }

    /// <summary>
    /// Envía un correo de verificación de email
    /// </summary>
    /// <param name="request">Solicitud de verificación de email</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta con ID de seguimiento del correo</returns>
    /// <response code="202">Correo encolado exitosamente para envío</response>
    /// <response code="400">Solicitud inválida</response>
    /// <response code="500">Error interno del servidor</response>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(VerifyEmailResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendVerificationEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var outcome = await _sendVerificationEmailUseCase.ExecuteAsync(request, cancellationToken);

        if (!outcome.IsSuccess)
            return BadRequest(new { message = outcome.Message, errors = outcome.Errors });

        var response = new VerifyEmailResponse
        {
            MailJobId = outcome.MailJobId,
            Success = true,
            Message = outcome.Message
        };

        return AcceptedAtAction(nameof(SendVerificationEmail), response);
    }

    /// <summary>
    /// Envía un correo de reset de contraseña
    /// </summary>
    /// <param name="request">Solicitud de reset de contraseña</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta con ID de seguimiento del correo</returns>
    /// <response code="202">Correo encolado exitosamente para envío</response>
    /// <response code="400">Solicitud inválida</response>
    /// <response code="500">Error interno del servidor</response>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ResetPasswordResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendPasswordReset(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var outcome = await _sendPasswordResetUseCase.ExecuteAsync(request, cancellationToken);

        if (!outcome.IsSuccess)
            return BadRequest(new { message = outcome.Message, errors = outcome.Errors });

        var response = new ResetPasswordResponse
        {
            MailJobId = outcome.MailJobId,
            Success = true,
            Message = outcome.Message
        };

        return AcceptedAtAction(nameof(SendPasswordReset), response);
    }

    /// <summary>
    /// Envía una notificación de cita
    /// </summary>
    /// <param name="request">Solicitud de notificación de cita</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta con ID de seguimiento del correo</returns>
    /// <response code="202">Correo encolado exitosamente para envío</response>
    /// <response code="400">Solicitud inválida</response>
    /// <response code="500">Error interno del servidor</response>
    [HttpPost("appointment-notification")]
    [ProducesResponseType(typeof(AppointmentNotificationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendAppointmentNotification(
        [FromBody] AppointmentNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var outcome = await _sendAppointmentNotificationUseCase.ExecuteAsync(request, cancellationToken);

        if (!outcome.IsSuccess)
            return BadRequest(new { message = outcome.Message, errors = outcome.Errors });

        var response = new AppointmentNotificationResponse
        {
            MailJobId = outcome.MailJobId,
            Success = true,
            Message = outcome.Message
        };

        return AcceptedAtAction(nameof(SendAppointmentNotification), response);
    }
}
