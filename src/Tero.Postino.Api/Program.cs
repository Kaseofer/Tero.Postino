using RabbitMQ.Client;
using Tero.Postino.Application.Authorization;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Email.UseCases;
using Tero.Postino.Infrastructure.Authorization;
using Tero.Postino.Infrastructure.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Tero Service Authentication Configuration
var authorizedServices = new Dictionary<string, string>
{
    // Formato: { "service-id", "service-token" }
    // En producción, estos valores deben venir de un servicio seguro de configuración
    // o variables de entorno
    { "auth-api", builder.Configuration["Tero:Services:AuthApi:Token"] ?? "auth-api-token-replace-me" },
    { "appointments-api", builder.Configuration["Tero:Services:AppointmentsApi:Token"] ?? "appointments-api-token-replace-me" },
    { "whatsapp-gateway", builder.Configuration["Tero:Services:WhatsAppGateway:Token"] ?? "whatsapp-gateway-token-replace-me" }
};

builder.Services.AddSingleton<IServiceIdentityValidator>(sp =>
    new TeroServiceIdentityValidator(authorizedServices, sp.GetRequiredService<ILogger<TeroServiceIdentityValidator>>()));

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

// Application Use Cases
builder.Services.AddScoped<ISendVerificationEmailUseCase, SendVerificationEmailUseCase>();
builder.Services.AddScoped<ISendPasswordResetUseCase, SendPasswordResetUseCase>();
builder.Services.AddScoped<ISendAppointmentNotificationUseCase, SendAppointmentNotificationUseCase>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Registrar middleware de Correlation ID temprano en la pipeline
app.UseCorrelationId();

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
