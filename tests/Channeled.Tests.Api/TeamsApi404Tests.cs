using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class TeamsApi404Tests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _ownerClient = null!;
    private HttpClient _outsiderClient = null!;
    private string _teamSlug = "";
    private Guid _outsiderId;

    public TeamsApi404Tests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var outsider = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = "outsider@example.com",
            UserName = "outsider@example.com",
            DisplayName = "Outsider",
                    UsernameSlug = "outsider",
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(outsider, "OutsiderPass1!")).Succeeded.Should().BeTrue();
        _outsiderId = outsider.Id;

        _ownerClient = CreateClient();
        _outsiderClient = CreateClient();

        (await _ownerClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@example.com", "OwnerPassword1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_ownerClient);

        (await _outsiderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("outsider@example.com", "OutsiderPass1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_outsiderClient);

        var created = await _ownerClient.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Acme", "test"));
        created.EnsureSuccessStatusCode();
        var team = await created.Content.ReadFromJsonAsync<TeamDto>();
        _teamSlug = team!.Slug;

        var channel = await _ownerClient.PostAsJsonAsync(
            $"/api/v1/teams/{_teamSlug}/channels",
            new CreateChannelRequest("secret", null, ChannelVisibility.Private, ChannelKind.File));
        channel.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("GET", "/api/v1/teams/{slug}")]
    [InlineData("GET", "/api/v1/teams/{slug}/members")]
    [InlineData("GET", "/api/v1/teams/{slug}/channels")]
    [InlineData("GET", "/api/v1/teams/{slug}/channels/secret")]
    [InlineData("GET", "/api/v1/teams/{slug}/channels/secret/members")]
    public async Task Hidden_team_resources_return_404_for_outsider(string method, string template)
    {
        var path = template.Replace("{slug}", _teamSlug, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await _outsiderClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task Unknown_slug_returns_404_not_403()
    {
        var response = await _ownerClient.GetAsync("/api/v1/teams/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Private_channel_hidden_from_team_member_without_membership()
    {
        // Invite outsider into team as member, but not into private channel
        var invite = await _ownerClient.PostAsJsonAsync(
            $"/api/v1/teams/{_teamSlug}/invitations",
            new CreateTeamInvitationRequest("outsider@example.com", MemberRole.Member));
        invite.EnsureSuccessStatusCode();
        var invitation = await invite.Content.ReadFromJsonAsync<TeamInvitationDto>();

        var accept = await _outsiderClient.PostAsJsonAsync(
            "/api/v1/teams/invitations/accept",
            new AcceptTeamInvitationRequest(invitation!.RawToken!));
        accept.EnsureSuccessStatusCode();

        var hidden = await _outsiderClient.GetAsync($"/api/v1/teams/{_teamSlug}/channels/secret");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await _outsiderClient.GetAsync($"/api/v1/teams/{_teamSlug}/channels");
        listed.EnsureSuccessStatusCode();
        var channels = await listed.Content.ReadFromJsonAsync<List<ChannelDto>>();
        channels!.Any(c => c.Name == "secret").Should().BeFalse();
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task AttachCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
