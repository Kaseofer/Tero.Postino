using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Tero.Postino.Api.Authentication;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Email.UseCases;
using Tero.Postino.Application.Reminders;
using Tero.Postino.Application.Reminders.Ports;
using Tero.Postino.Infrastructure.Configuration;
using Tero.Postino.Infrastructure.Email;
using Tero.Postino.Infrastructure.RabbitMq;
using Tero.Postino.Infrastructure.Reminders;
using Tero.ServiceDefaults.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Reemplaza el mecanismo anterior (headers estáticos X-Tero-Service-Id/X-Tero-Service-Token,
// ver README) por el mismo JWT de servicio que emite Auth y ya validan Appointments/Gateway.
builder.Services.AddTeroJwtAuthentication(builder.Configuration);

// RabbitMQ Configuration
builder.Services.AddSingleton(sp =>
{
    var cfg = builder.Configuration;
    return new ConnectionFactory
    {
        HostName = cfg["Rabbit:Host"] ?? "localhost",
        UserName = cfg["Rabbit:User"] ?? "guest",
        Password = cfg["Rabbit:Password"] ?? "guest",
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };
});

// Infrastructure Services
builder.Services.AddSingleton<IMailPublisher, MailPublisher>();

// El otro extremo de la cola que llena MailPublisher (acá) y Tero.Auth.Api.QueueEmailSender:
// hasta esta task nadie la consumía y los mails quedaban encolados para siempre.
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<MailJournalOptions>(builder.Configuration.GetSection(MailJournalOptions.SectionName));
builder.Services.AddSingleton<MailJournalWriter>();
builder.Services.AddSingleton<SmtpMailSender>();
builder.Services.AddSingleton<MailTemplateRenderer>();
builder.Services.AddHostedService<MailQueueConsumer>();

// Application Use Cases
builder.Services.AddScoped<ISendMailUseCase, SendMailUseCase>();

// POST-01: job de recordatorios. Options — sin AddValidatedOptions acá (a diferencia del
// Gateway): un tenant mal configurado en Reminders:TenantIds falla recién en la corrida del
// job (loguea y sigue con el próximo tenant), no tira abajo el host entero al arrancar.
builder.Services.Configure<AppointmentsOptions>(builder.Configuration.GetSection(AppointmentsOptions.SectionName));
builder.Services.Configure<WhatsAppGatewayOptions>(builder.Configuration.GetSection(WhatsAppGatewayOptions.SectionName));
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection(ReminderOptions.SectionName));

// AuthServiceTokenClient centralizado en Tero.ServiceDefaults (backlog de Tero.Shared,
// SH-P1-1) — antes era una copia propia acá.
builder.Services.AddTeroAuthServiceTokenClient(builder.Configuration);

builder.Services.AddHttpClient<IAppointmentsReminderClient, AppointmentsReminderClient>((sp, client) =>
{
    var appointmentsOptions = sp.GetRequiredService<IOptions<AppointmentsOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(appointmentsOptions.BaseUrl));
});
builder.Services.AddHttpClient<IWhatsAppGatewayClient, WhatsAppGatewayClient>((sp, client) =>
{
    var gatewayOptions = sp.GetRequiredService<IOptions<WhatsAppGatewayOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(gatewayOptions.BaseUrl));
});

builder.Services.AddScoped<SendAppointmentRemindersUseCase>();
builder.Services.AddHostedService<ReminderBackgroundService>();

var app = builder.Build();

// Falla explícito al arrancar si falta una plantilla para algún MailNotificationType en algún
// idioma ya cargado — mejor que enterarse en el primer envío de ese tipo/idioma, con el mail
// ya perdido salvo el fallback genérico (ver MailTemplateRenderer).
app.Services.GetRequiredService<MailTemplateRenderer>().ValidateTemplatesExistOrThrow();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Registrar middleware de Correlation ID temprano en la pipeline
app.UseCorrelationId();

app.UseHttpsRedirection();

// Faltaba: sin este middleware, [Authorize] no tiene con qué resolver el principal — mismo
// gap encontrado hoy en Tero.Auth.Api (UseRateLimiter) y Tero.WhatsApp.Gateway
// (UseAuthentication, WA-01). Este servicio no había tenido ningún endpoint con [Authorize]
// hasta ahora (usaba el filtro estático viejo).
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();

static string EnsureTrailingSlash(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
