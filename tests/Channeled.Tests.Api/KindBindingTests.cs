using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class KindBindingTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _client = null!;
    private string _slug = "";

    public KindBindingTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("kind@example.com", "Owner", "OwnerPassword1!"));
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        (await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("kind@example.com", "OwnerPassword1!")))
            .EnsureSuccessStatusCode();
        var csrf = await _client.GetAsync("/api/v1/auth/csrf");
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
        var team = await (await _client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("KindTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Raw_json_kind_1_creates_text_channel()
    {
        const string payload = """{"name":"talkroom","description":null,"visibility":0,"kind":1}""";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"/api/v1/teams/{_slug}/channels", content);
        var raw = await resp.Content.ReadAsStringAsync();
        resp.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(raw))
        {
            doc.RootElement.GetProperty("kind").GetInt32().Should().Be(1);
        }

        var listRaw = await _client.GetStringAsync($"/api/v1/teams/{_slug}/channels");
        using var listDoc = JsonDocument.Parse(listRaw);
        var talk = listDoc.RootElement.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "talkroom");
        talk.GetProperty("kind").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Missing_kind_is_rejected()
    {
        const string payload = """{"name":"nokind","description":null,"visibility":0}""";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync($"/api/v1/teams/{_slug}/channels", content);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var raw = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        doc.RootElement.GetProperty("code").GetString().Should().Be("validation_error");
    }
}
