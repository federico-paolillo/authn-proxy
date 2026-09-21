using AuthnProxy.Application.Ping.Handlers;
using AuthnProxy.Application.Ping.Handlers.Defaults;

using Microsoft.Extensions.DependencyInjection;

namespace AuthnProxy.Application.Ping.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPing(this IServiceCollection services)
    {
        services.AddSingleton<IPingHandler, PingHandler>();

        return services;
    }
}