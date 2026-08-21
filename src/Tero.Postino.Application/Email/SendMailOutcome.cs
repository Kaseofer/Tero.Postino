namespace Tero.Postino.Application.Email;

/// <summary>Reemplaza a las tres salidas que existían antes (una por tipo de notificación) —
/// eran idénticas entre sí.</summary>
public sealed record SendMailOutcome
{
    public required string MailJobId { get; init; }

    public required bool IsSuccess { get; init; }

    public required string Message { get; init; }

    public List<string> Errors { get; init; } = [];
}
