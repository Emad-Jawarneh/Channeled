using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class HealthEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public HealthEndpointTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_liveness_returns_ok_without_dependency_probe()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LivenessBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.Api.Should().BeTrue();
    }

    [Fact]
    public async Task Api_health_reports_database_and_store()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.Database.Should().BeTrue();
        body.Store.Should().BeTrue();
    }

    private sealed record LivenessBody(string Status, bool Api);

    private sealed record HealthBody(string Status, bool Api, bool Database, bool Store, bool Disk);
}
