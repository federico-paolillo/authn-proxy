namespace AuthnProxy.Api.Ping;

internal static class Endpoints
{
    public static RouteGroupBuilder MapPing(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/ping");

        group.MapGet("/", Handlers.GetPing)
            .WithName("GetPing");

        return group;
    }
}