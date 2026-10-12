using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Channeled.Tests.Api;

public class HealthEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public HealthEndpointTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
    }

    private sealed record HealthBody(string Status, string? Store);
}
