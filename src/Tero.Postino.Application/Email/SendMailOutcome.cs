namespace Tero.Postino.Application.Email;

/// <summary>Reemplaza a las tres salidas que existían antes (una por tipo de notificación) —
/// eran idénticas entre sí.</summary>
public sealed record SendMailOutcome
{
    public required string MailJobId { get; init; }

    public required bool IsSuccess { get; init; }

    public required string Message { get; init; }

    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Permite que la frontera HTTP diferencie un request inválido de una indisponibilidad
    /// transitoria de RabbitMQ. Antes ambos caminos terminaban como 400 y los callers no
    /// tenían forma de decidir si correspondía reintentar.
    /// </summary>
    public SendMailFailureKind FailureKind { get; init; }
}

public enum SendMailFailureKind
{
    None = 0,
    Validation = 1,
    Infrastructure = 2,
}
