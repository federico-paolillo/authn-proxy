using AuthnProxy.Application.Ping.Models;

namespace AuthnProxy.Application.Ping.Handlers.Defaults;

public sealed class PingHandler(TimeProvider timeProvider) : IPingHandler
{
    public PingResult Execute()
    {
        long timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();

        return new PingResult(timestamp);
    }
}