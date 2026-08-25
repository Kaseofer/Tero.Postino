namespace Tero.Postino.Infrastructure.Configuration;

public sealed class RabbitMqOptions
{
    public const string SectionName = "Rabbit";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
}
