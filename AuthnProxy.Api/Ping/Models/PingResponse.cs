using AuthnProxy.Application.Ping.Models;

namespace AuthnProxy.Api.Ping.Models;

public sealed record PingResponse(long Timestamp)
{
    public static PingResponse From(PingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PingResponse(result.Timestamp);
    }
}