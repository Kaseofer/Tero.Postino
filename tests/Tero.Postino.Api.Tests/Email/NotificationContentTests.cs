using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Email.UseCases;
using Tero.Postino.Infrastructure.Email;

namespace Tero.Postino.Api.Tests.Email;

public sealed class NotificationContentTests
{
    [Fact]
    public async Task ExecuteAsync_Cancelacion_ConservaElMotivoEnElModelo()
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);
        var notification = new AppointmentCancelledNotification
        {
            RecipientEmail = "persona@example.com",
            RecipientName = "Ana",
            ServiceName = "Consulta",
            AppointmentDateTime = DateTime.UtcNow.AddHours(2),
            CancellationReason = "El profesional no estará disponible",
        };

        var outcome = await useCase.ExecuteAsync(notification);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(
            notification.CancellationReason,
            publisher.Message!.TemplateModel!["cancellationReason"]);
    }

    [Fact]
    public async Task ExecuteAsync_Reprogramacion_ConservaElHorarioAnteriorEnElModelo()
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);
        var previous = DateTime.UtcNow.AddDays(1);
        var notification = new AppointmentRescheduledNotification
        {
            RecipientEmail = "persona@example.com",
            RecipientName = "Ana",
            ServiceName = "Consulta",
            PreviousAppointmentDateTime = previous,
            AppointmentDateTime = previous.AddHours(2),
        };

        var outcome = await useCase.ExecuteAsync(notification);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(
            previous,
            publisher.Message!.TemplateModel!["previousAppointmentDateTime"]);
    }

    [Theory]
    [InlineData("es", "Motivo de la cancelación")]
    [InlineData("en", "Reason for cancellation")]
    [InlineData("pt", "Motivo do cancelamento")]
    public void Render_Cancelacion_MuestraElMotivoEnCadaIdioma(string language, string label)
    {
        var renderer = CreateRenderer();
        var model = JsonModel(new Dictionary<string, object>
        {
            ["contactName"] = "Ana",
            ["serviceName"] = "Consulta",
            ["appointmentDateTime"] = "25/08/2026 15:00",
            ["cancellationReason"] = "Cambio de agenda",
        });

        var html = renderer.Render("AppointmentCancelled", language, model);

        Assert.Contains(label, html);
        Assert.Contains("Cambio de agenda", html);
        Assert.DoesNotContain("{{cancellationReason}}", html);
    }

    [Theory]
    [InlineData("es", "Horario anterior")]
    [InlineData("en", "Previous date and time")]
    [InlineData("pt", "Data e horário anteriores")]
    public void Render_Reprogramacion_MuestraHorarioAnteriorEnCadaIdioma(string language, string label)
    {
        var renderer = CreateRenderer();
        var model = JsonModel(new Dictionary<string, object>
        {
            ["contactName"] = "Ana",
            ["serviceName"] = "Consulta",
            ["appointmentDateTime"] = "25/08/2026 17:00",
            ["previousAppointmentDateTime"] = "25/08/2026 15:00",
        });

        var html = renderer.Render("AppointmentRescheduled", language, model);

        Assert.Contains(label, html);
        Assert.Contains("25/08/2026 15:00", html);
        Assert.DoesNotContain("{{previousAppointmentDateTime}}", html);
    }

    [Theory]
    [InlineData("@")]
    [InlineData("persona@")]
    [InlineData("@dominio.com")]
    public async Task ExecuteAsync_EmailInvalido_NoPublica(string email)
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreatePasswordReset(email: email));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("El formato del correo no es válido", outcome.Errors);
        Assert.Null(publisher.Message);
    }

    [Theory]
    [InlineData("/reset")]
    [InlineData("ftp://app.tero.test/reset")]
    public async Task ExecuteAsync_UrlDeAccionInvalida_NoPublica(string actionUrl)
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreatePasswordReset(actionUrl: actionUrl));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("La URL de acción debe ser una URL HTTP o HTTPS absoluta", outcome.Errors);
        Assert.Null(publisher.Message);
    }

    [Fact]
    public async Task ExecuteAsync_TokenVacio_NoPublica()
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreatePasswordReset(token: " "));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("El token de acción es obligatorio", outcome.Errors);
        Assert.Null(publisher.Message);
    }

    [Fact]
    public async Task ExecuteAsync_UrlConQueryYFragment_UbicaElTokenAntesDelFragmento()
    {
        var publisher = new CapturingPublisher();
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreatePasswordReset(
            actionUrl: "https://app.tero.test/reset?lang=es#paso",
            token: "a+b="));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(
            "https://app.tero.test/reset?lang=es&token=a%2Bb%3D#paso",
            publisher.Message!.TemplateModel!["resetUrl"]);
    }

    [Fact]
    public async Task PlantillasVisuales_ConModeloGeneradoPorPostino_NoDejanPlaceholders()
    {
        var renderer = CreateRenderer();

        foreach (var notification in CreateAllNotificationTypes())
        {
            var publisher = new CapturingPublisher();
            var outcome = await CreateUseCase(publisher).ExecuteAsync(notification);

            Assert.True(outcome.IsSuccess);
            var message = Assert.IsType<MailMessageDto>(publisher.Message);
            foreach (var language in new[] { "es", "en", "pt" })
            {
                var html = renderer.Render(message.TemplateType, language, JsonModel(message.TemplateModel!));

                Assert.DoesNotContain("{{", html);
                Assert.DoesNotContain("::optional:", html);
                Assert.DoesNotContain("::/optional::", html);
            }
        }
    }

    private static SendMailUseCase CreateUseCase(IMailPublisher publisher) =>
        new(publisher, NullLogger<SendMailUseCase>.Instance);

    private static MailTemplateRenderer CreateRenderer() =>
        new(NullLogger<MailTemplateRenderer>.Instance);

    private static Dictionary<string, JsonElement> JsonModel(Dictionary<string, object> model) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(model))!;

    private static PasswordResetNotification CreatePasswordReset(
        string email = "persona@example.com",
        string actionUrl = "https://app.tero.test/reset",
        string token = "token") => new()
        {
            RecipientEmail = email,
            RecipientName = "Ana",
            ActionUrl = actionUrl,
            Token = token,
            ExpirationMinutes = 60,
        };

    private static MailNotification[] CreateAllNotificationTypes()
    {
        var appointmentDateTime = DateTime.UtcNow.AddDays(2);

        return
        [
            new AppointmentBookedNotification
            {
                RecipientEmail = "persona@example.com",
                RecipientName = "Ana",
                ServiceName = "Consulta",
                AppointmentDateTime = appointmentDateTime,
                OrganizationName = "Centro Tero",
                OrganizationPhone = "+54 11 5555-0000",
                OrganizationWhatsApp = "+54 11 5555-0000",
                ProfessionalName = "Dra. Pérez",
                Specialty = "Clínica médica",
                AppointmentUrl = "https://app.tero.test/turnos/1",
            },
            new AppointmentReminderNotification
            {
                RecipientEmail = "persona@example.com",
                RecipientName = "Ana",
                ServiceName = "Consulta",
                AppointmentDateTime = appointmentDateTime,
            },
            new AppointmentCancelledNotification
            {
                RecipientEmail = "persona@example.com",
                RecipientName = "Ana",
                ServiceName = "Consulta",
                AppointmentDateTime = appointmentDateTime,
                CancellationReason = "Cambio de agenda",
            },
            new AppointmentRescheduledNotification
            {
                RecipientEmail = "persona@example.com",
                RecipientName = "Ana",
                ServiceName = "Consulta",
                PreviousAppointmentDateTime = appointmentDateTime,
                AppointmentDateTime = appointmentDateTime.AddHours(2),
            },
            CreatePasswordReset(),
            new EmailVerificationNotification
            {
                RecipientEmail = "persona@example.com",
                RecipientName = "Ana",
                ActionUrl = "https://app.tero.test/verificar",
                Token = "token",
            },
            new AdminCredentialsNotification
            {
                RecipientEmail = "admin@example.com",
                RecipientName = "Admin",
                TenantName = "Centro Tero",
                ActionUrl = "https://app.tero.test/clave",
                Token = "token",
                ExpirationMinutes = 60,
            },
        ];
    }

    private sealed class CapturingPublisher : IMailPublisher
    {
        public MailMessageDto? Message { get; private set; }

        public Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }
}
