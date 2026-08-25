using Tero.Postino.Infrastructure.RabbitMq;

namespace Tero.Postino.Api.Tests.RabbitMq;

public sealed class RabbitMqChannelOptionsTests
{
    [Fact]
    public void CreatePublisherConfirmed_EnablesConfirmationsAndTracking()
    {
        var options = RabbitMqChannelOptions.CreatePublisherConfirmed();

        Assert.True(options.PublisherConfirmationsEnabled);
        Assert.True(options.PublisherConfirmationTrackingEnabled);
        Assert.Equal(TimeSpan.FromSeconds(30), RabbitMqChannelOptions.PublishConfirmationTimeout);
    }
}
