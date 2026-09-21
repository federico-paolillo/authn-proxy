using AuthnProxy.Api.Ping.Models;
using AuthnProxy.Application.Ping.Handlers;
using AuthnProxy.Application.Ping.Models;

using Microsoft.AspNetCore.Http.HttpResults;

namespace AuthnProxy.Api.Ping;

internal static class Handlers
{
    public static Ok<PingResponse> GetPing(IPingHandler handler)
    {
        PingResult result = handler.Execute();

        return TypedResults.Ok(PingResponse.From(result));
    }
}