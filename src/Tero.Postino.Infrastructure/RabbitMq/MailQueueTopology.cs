namespace Tero.Postino.Infrastructure.RabbitMq;

/// <summary>
/// Nombres compartidos por el publicador y el consumidor de correo.
/// </summary>
internal static class MailQueueTopology
{
    public const string ExchangeName = "postino.mail";
    public const string QueueName = "postino.mail.queue";
    public const string RoutingKey = "mail.send";
    public const string DeadQueueName = "postino.mail.dead";
    public const string DeadRoutingKey = "mail.dead";
    public const string RetryCountHeader = "x-retry-count";
}
