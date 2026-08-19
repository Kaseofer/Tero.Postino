using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Tero.Postino.Infrastructure.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton(sp =>
{
    var cfg = builder.Configuration;
    return new ConnectionFactory
    {
        HostName = cfg["Rabbit:Host"] ?? "localhost",
        UserName = cfg["Rabbit:User"] ?? "guest",
        Password = cfg["Rabbit:Password"] ?? "guest"
    };
});

builder.Services.AddSingleton<MailPublisher>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
