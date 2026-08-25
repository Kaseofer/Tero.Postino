using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tero.Contracts.Mail.Requests;
using Tero.Postino.Application.Email;
using Tero.Postino.Application.Email.Ports;
using Tero.Postino.Application.Email.UseCases;
using Tero.Postino.Controllers;
using Tero.ServiceDefaults.CorrelationId;

namespace Tero.Postino.Api.Tests.Email;

public sealed class SendMailUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WithInvalidEmail_ReturnsValidationFailure()
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
    public async Task ExecuteAsync_WhenRabbitMqFails_ReturnsInfrastructureFailureWithoutLeakingDetails()
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
    public async Task ExecuteAsync_WhenCancelled_PropagatesCancellation()
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
    [Fact]
    public async Task Send_WithoutTenantIdentity_ReturnsForbiddenWithoutDispatching()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("client_id", Guid.NewGuid().ToString())],
                authenticationType: "test")),
        };
        var stub = new StubSendMailUseCase(new SendMailOutcome
        {
            MailJobId = "unused",
            IsSuccess = true,
            Message = "unused",
        });
        var controller = new MailController(
            stub,
            new CorrelationIdContext(
                new HttpContextAccessor { HttpContext = httpContext },
                new CorrelationIdOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await controller.Send(SendMailUseCaseTests.CreateNotification(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Null(stub.RequestContext);
    }

    [Theory]
    [InlineData(SendMailFailureKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(SendMailFailureKind.Infrastructure, StatusCodes.Status503ServiceUnavailable)]
    public async Task Send_MapsFailureKindToExpectedHttpStatus(
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
        var callerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("client_id", callerId.ToString()),
                    new Claim("tenant_id", tenantId.ToString()),
                ],
                authenticationType: "test")),
        };
        httpContext.Items["CorrelationId"] = "correlation-123";
        var stub = new StubSendMailUseCase(outcome);
        var correlationContext = new CorrelationIdContext(
            new HttpContextAccessor { HttpContext = httpContext },
            new CorrelationIdOptions());
        var controller = new MailController(stub, correlationContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        var result = await controller.Send(
            SendMailUseCaseTests.CreateNotification(),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Equal(tenantId.ToString("D"), stub.RequestContext!.TenantId);
        Assert.Equal(callerId.ToString("D"), stub.RequestContext.CallerClientId);
        Assert.Equal("correlation-123", stub.RequestContext.CorrelationId);
    }

    private sealed class StubSendMailUseCase(SendMailOutcome outcome) : ISendMailUseCase
    {
        public MailRequestContext? RequestContext { get; private set; }

        public Task<SendMailOutcome> ExecuteAsync(
            MailNotification notification,
            CancellationToken cancellationToken = default,
            MailRequestContext? requestContext = null)
        {
            RequestContext = requestContext;
            return Task.FromResult(outcome);
        }
    }
}
