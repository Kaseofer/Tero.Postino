using Microsoft.Extensions.Logging.Abstractions;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Reminders;
using Tero.Postino.Application.Reminders.Ports;

namespace Tero.Postino.Api.Tests.Email;

public sealed class ReminderClaimTests
{
    [Fact]
    public async Task AllRequestedChannelsSucceed_CompletesClaim()
    {
        var appointments = new FakeAppointmentsClient(CreateCandidate(email: true, whatsApp: true));
        var email = EmailSuccess();
        var tenantId = Guid.NewGuid();
        var useCase = CreateUseCase(appointments, email, new FakeWhatsAppClient());

        await useCase.ExecuteForTenantAsync(tenantId, 24);

        Assert.Equal(1, appointments.CompletedCount);
        Assert.Equal(0, appointments.ReleasedCount);
        Assert.Equal(tenantId.ToString("D"), email.RequestContext!.TenantId);
        Assert.Equal("postino-reminder-worker", email.RequestContext.CallerClientId);
    }

    [Fact]
    public async Task RequestedChannelFails_ReleasesClaim()
    {
        var appointments = new FakeAppointmentsClient(CreateCandidate(email: true));
        var useCase = CreateUseCase(appointments, EmailFailure(), new FakeWhatsAppClient());

        await useCase.ExecuteForTenantAsync(Guid.NewGuid(), 24);

        Assert.Equal(0, appointments.CompletedCount);
        Assert.Equal(1, appointments.ReleasedCount);
    }

    [Fact]
    public async Task LocalizedCandidate_UsesOrganizationLanguageTimeZoneAndServiceData()
    {
        var startsAtUtc = new DateTime(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc);
        var candidate = CreateCandidate(
            email: true,
            whatsApp: true,
            startsAtUtc: startsAtUtc,
            languageCode: "pt-BR",
            timeZoneId: "America/Argentina/Buenos_Aires",
            serviceName: "Consulta clínica",
            location: "Av. Siempre Viva 123",
            durationMinutes: 45);
        var email = EmailSuccess();
        var whatsApp = new FakeWhatsAppClient();
        var useCase = CreateUseCase(new FakeAppointmentsClient(candidate), email, whatsApp);

        await useCase.ExecuteForTenantAsync(Guid.NewGuid(), 24);

        var notification = Assert.IsType<AppointmentReminderNotification>(email.Notification);
        Assert.Equal(startsAtUtc, notification.AppointmentDateTime);
        Assert.Equal("pt-BR", notification.LanguageCode);
        Assert.Equal("Consulta clínica", notification.ServiceName);
        Assert.Equal("Av. Siempre Viva 123", notification.Location);
        Assert.Equal(45, notification.DurationMinutes);
        Assert.Equal("Dra. Pérez", notification.ProfessionalName);
        Assert.Equal(candidate.TimeZoneId, email.RequestContext!.RecipientTimeZoneId);
        Assert.Equal("pt-BR", whatsApp.LanguageCode);
        Assert.Equal("26/08 15:00", whatsApp.BodyVariables![2]);
    }

    [Fact]
    public async Task RequestedChannelWithoutContact_ReleasesClaim()
    {
        var appointments = new FakeAppointmentsClient(CreateCandidate(whatsApp: true, whatsAppPhone: null));
        var whatsApp = new FakeWhatsAppClient();
        var useCase = CreateUseCase(appointments, EmailSuccess(), whatsApp);

        await useCase.ExecuteForTenantAsync(Guid.NewGuid(), 24);

        Assert.Equal(0, appointments.CompletedCount);
        Assert.Equal(1, appointments.ReleasedCount);
        Assert.Equal(0, whatsApp.SendCount);
    }

    [Fact]
    public async Task NoChannelsEnabled_CompletesWithoutSending()
    {
        var appointments = new FakeAppointmentsClient(CreateCandidate());
        var email = EmailSuccess();
        var whatsApp = new FakeWhatsAppClient();
        var useCase = CreateUseCase(appointments, email, whatsApp);

        await useCase.ExecuteForTenantAsync(Guid.NewGuid(), 24);

        Assert.Equal(1, appointments.CompletedCount);
        Assert.Equal(0, appointments.ReleasedCount);
        Assert.Equal(0, email.SendCount);
        Assert.Equal(0, whatsApp.SendCount);
    }

    [Fact]
    public async Task CancellationRequested_AttemptsReleaseAndPropagates()
    {
        var appointments = new FakeAppointmentsClient(CreateCandidate(email: true));
        var email = new FakeEmailUseCase((_, token) => Task.FromCanceled<SendMailOutcome>(token));
        var useCase = CreateUseCase(appointments, email, new FakeWhatsAppClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteForTenantAsync(Guid.NewGuid(), 24, cancellation.Token));

        Assert.Equal(1, appointments.ReleasedCount);
    }

    private static SendAppointmentRemindersUseCase CreateUseCase(
        IAppointmentsReminderClient appointments,
        ISendMailUseCase email,
        IWhatsAppGatewayClient whatsApp) =>
        new(appointments, whatsApp, email, NullLogger<SendAppointmentRemindersUseCase>.Instance);

    private static FakeEmailUseCase EmailSuccess() => new((_, _) => Task.FromResult(new SendMailOutcome
    {
        MailJobId = "mail-job",
        IsSuccess = true,
        Message = "ok",
        FailureKind = SendMailFailureKind.None,
    }));

    private static FakeEmailUseCase EmailFailure() => new((_, _) => Task.FromResult(new SendMailOutcome
    {
        MailJobId = "mail-job",
        IsSuccess = false,
        Message = "RabbitMQ no disponible",
        FailureKind = SendMailFailureKind.Infrastructure,
    }));

    private static ReminderCandidate CreateCandidate(
        bool email = false,
        bool whatsApp = false,
        string? whatsAppPhone = "+5491155550000",
        DateTime? startsAtUtc = null,
        string languageCode = "es",
        string timeZoneId = "America/Argentina/Buenos_Aires",
        string? serviceName = "Consulta",
        string? location = null,
        int durationMinutes = 30) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startsAtUtc ?? DateTime.UtcNow.AddHours(2),
            "Dra. Pérez",
            Guid.NewGuid(),
            "Ana",
            "ana@example.com",
            whatsAppPhone,
            email,
            whatsApp,
            durationMinutes,
            serviceName,
            location,
            languageCode,
            timeZoneId);

    private sealed class FakeAppointmentsClient(ReminderCandidate candidate) : IAppointmentsReminderClient
    {
        public int CompletedCount { get; private set; }
        public int ReleasedCount { get; private set; }

        public Task<IReadOnlyList<ReminderCandidate>> ClaimPendingRemindersAsync(
            Guid tenantId,
            int windowHours,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderCandidate>>([candidate]);

        public Task CompleteReminderClaimAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid claimToken,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(candidate.AppointmentId, appointmentId);
            Assert.Equal(candidate.ClaimToken, claimToken);
            CompletedCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseReminderClaimAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid claimToken,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(candidate.AppointmentId, appointmentId);
            Assert.Equal(candidate.ClaimToken, claimToken);
            ReleasedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailUseCase(
        Func<MailNotification, CancellationToken, Task<SendMailOutcome>> send) : ISendMailUseCase
    {
        public int SendCount { get; private set; }
        public MailRequestContext? RequestContext { get; private set; }
        public MailNotification? Notification { get; private set; }

        public Task<SendMailOutcome> ExecuteAsync(
            MailNotification notification,
            CancellationToken cancellationToken = default,
            MailRequestContext? requestContext = null)
        {
            SendCount++;
            RequestContext = requestContext;
            Notification = notification;
            return send(notification, cancellationToken);
        }
    }

    private sealed class FakeWhatsAppClient : IWhatsAppGatewayClient
    {
        public int SendCount { get; private set; }
        public string? LanguageCode { get; private set; }
        public IReadOnlyList<string>? BodyVariables { get; private set; }

        public Task SendReminderAsync(
            Guid tenantId,
            string to,
            string idempotencyKey,
            string languageCode,
            IReadOnlyList<string> bodyVariables,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            LanguageCode = languageCode;
            BodyVariables = bodyVariables;
            return Task.CompletedTask;
        }
    }
}
