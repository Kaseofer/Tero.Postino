namespace Tero.Postino.Application.Email;

/// <summary>
/// Resultado del caso de uso SendVerificationEmailUseCase
/// </summary>
public sealed record SendVerificationEmailOutcome
{
    /// <summary>
    /// Identificador único del trabajo de correo
    /// </summary>
    public required string MailJobId { get; init; }

    /// <summary>
    /// Indica si la solicitud fue procesada exitosamente
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Mensaje descriptivo del resultado
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Errores si ocurrieron durante la validación
    /// </summary>
    public List<string> Errors { get; init; } = [];
}
