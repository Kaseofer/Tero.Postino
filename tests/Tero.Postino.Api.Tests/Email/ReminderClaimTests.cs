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
        string? whatsAppPhone = "+5491155550000") => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(2),
            "Dra. Pérez",
            Guid.NewGuid(),
            "Ana",
            "ana@example.com",
            whatsAppPhone,
            email,
            whatsApp);

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

        public Task<SendMailOutcome> ExecuteAsync(
            MailNotification notification,
            CancellationToken cancellationToken = default,
            MailRequestContext? requestContext = null)
        {
            SendCount++;
            RequestContext = requestContext;
            return send(notification, cancellationToken);
        }
    }

    private sealed class FakeWhatsAppClient : IWhatsAppGatewayClient
    {
        public int SendCount { get; private set; }

        public Task SendReminderAsync(
            Guid tenantId,
            string to,
            string idempotencyKey,
            IReadOnlyList<string> bodyVariables,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }
}
