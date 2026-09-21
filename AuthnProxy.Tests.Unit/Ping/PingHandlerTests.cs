using AuthnProxy.Application.Ping.Handlers.Defaults;
using AuthnProxy.Application.Ping.Models;

namespace AuthnProxy.Tests.Unit.Ping;

public sealed class PingHandlerTests
{
    [Fact]
    public void ExecuteReturnsCurrentTimeAsUnixSeconds()
    {
        DateTimeOffset currentTime = new(2026, 9, 21, 10, 15, 30, TimeSpan.Zero);
        PingHandler handler = new(new FixedTimeProvider(currentTime));

        PingResult result = handler.Execute();

        Assert.Equal(currentTime.ToUnixTimeSeconds(), result.Timestamp);
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}