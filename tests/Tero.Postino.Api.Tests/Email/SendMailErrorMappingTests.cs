using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Email.UseCases;
using Tero.Postino.Controllers;

namespace Tero.Postino.Api.Tests.Email;

public sealed class SendMailUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ConEmailInvalido_DevuelveFalloDeValidacion()
    {
        var publisher = new StubMailPublisher();
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreateNotification("email-invalido"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(SendMailFailureKind.Validation, outcome.FailureKind);
        Assert.NotEmpty(outcome.Errors);
        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task ExecuteAsync_CuandoRabbitFalla_DevuelveFalloDeInfraestructuraSinFiltrarDetalle()
    {
        var publisher = new StubMailPublisher((_, _) => throw new InvalidOperationException("rabbit-password=secreto"));
        var useCase = CreateUseCase(publisher);

        var outcome = await useCase.ExecuteAsync(CreateNotification());

        Assert.False(outcome.IsSuccess);
        Assert.Equal(SendMailFailureKind.Infrastructure, outcome.FailureKind);
        Assert.Equal("El servicio de correo no está disponible temporalmente", outcome.Message);
        Assert.DoesNotContain("secreto", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public async Task ExecuteAsync_CuandoSeCancela_PropagaLaCancelacion()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var publisher = new StubMailPublisher((_, token) => Task.FromCanceled(token));
        var useCase = CreateUseCase(publisher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(CreateNotification(), cts.Token));
    }

    private static SendMailUseCase CreateUseCase(IMailPublisher publisher) =>
        new(publisher, NullLogger<SendMailUseCase>.Instance);

    internal static PasswordResetNotification CreateNotification(string email = "persona@example.com") => new()
    {
        RecipientEmail = email,
        RecipientName = "Persona",
        ActionUrl = "https://app.tero.test/reset",
        Token = "token",
        ExpirationMinutes = 60,
    };

    private sealed class StubMailPublisher(
        Func<MailMessageDto, CancellationToken, Task>? publish = null) : IMailPublisher
    {
        public int PublishCount { get; private set; }

        public Task PublishAsync(MailMessageDto message, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return publish?.Invoke(message, cancellationToken) ?? Task.CompletedTask;
        }
    }
}

public sealed class MailControllerTests
{
    [Theory]
    [InlineData(SendMailFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(SendMailFailureKind.Infrastructure, StatusCodes.Status503ServiceUnavailable)]
    public async Task Send_MapeaLaCategoriaDeFalloAlStatusHttpCorrecto(
        SendMailFailureKind failureKind,
        int expectedStatus)
    {
        var outcome = new SendMailOutcome
        {
            MailJobId = "message-id",
            IsSuccess = false,
            Message = "fallo",
            Errors = [],
            FailureKind = failureKind,
        };
        var controller = new MailController(new StubSendMailUseCase(outcome))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("client_id", Guid.NewGuid().ToString())],
                        authenticationType: "test")),
                },
            },
        };

        var result = await controller.Send(
            SendMailUseCaseTests.CreateNotification(),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
    }

    private sealed class StubSendMailUseCase(SendMailOutcome outcome) : ISendMailUseCase
    {
        public Task<SendMailOutcome> ExecuteAsync(
            MailNotification notification,
            CancellationToken cancellationToken = default) => Task.FromResult(outcome);
    }
}
