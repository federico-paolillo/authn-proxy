using System.Net;
using System.Net.Http.Json;

using AuthnProxy.Api.Ping.Models;

namespace AuthnProxy.Tests.Integration.Ping;

public sealed class PingEndpointTests(PingApiFactory factory) : IClassFixture<PingApiFactory>
{
    [Fact]
    public async Task GetPingReturnsCurrentUnixTimestamp()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/ping", UriKind.Relative));
        PingResponse? content = await response.Content.ReadFromJsonAsync<PingResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(content);
        Assert.Equal(PingApiFactory.CurrentTime.ToUnixTimeSeconds(), content.Timestamp);
    }
}