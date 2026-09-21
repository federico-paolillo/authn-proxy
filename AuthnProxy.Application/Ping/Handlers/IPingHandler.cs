using AuthnProxy.Application.Ping.Models;

namespace AuthnProxy.Application.Ping.Handlers;

public interface IPingHandler
{
    PingResult Execute();
}