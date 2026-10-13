using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Channeled.Api.Realtime;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ChannelHubTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;

    public ChannelHubTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Hub_exposes_only_JoinChannel_and_LeaveChannel()
    {
        var methods = typeof(ChannelHub)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.Name is not ("OnConnectedAsync" or "OnDisconnectedAsync"))
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        methods.Should().BeEquivalentTo(["JoinChannel", "LeaveChannel"]);
    }

    [Fact]
    public async Task Outsider_cannot_join_channel_group()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("hub-owner@example.com", "Owner", "OwnerPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Channeled.Infrastructure.Identity.AppUser>>();
        var outsider = new Channeled.Infrastructure.Identity.AppUser
        {
            Id = Channeled.Domain.Identity.UuidV7.NewGuid(),
            Email = "hub-out@example.com",
            UserName = "hub-out@example.com",
            DisplayName = "Out",
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(outsider, "OutsiderPass1!")).Succeeded.Should().BeTrue();

        var ownerClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
        (await ownerClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("hub-owner@example.com", "OwnerPassword1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(ownerClient);
        var team = await (await ownerClient.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("HubTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        (await ownerClient.PostAsJsonAsync(
            $"/api/v1/teams/{team!.Slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        var channels = await (await ownerClient.GetAsync($"/api/v1/teams/{team.Slug}/channels"))
            .Content.ReadFromJsonAsync<ChannelDto[]>();
        var channelId = channels!.Single(c => c.Name == "general").Id;

        typeof(ChannelHub).Should().BeAssignableTo<Hub>();
        channelId.Should().NotBeEmpty();
    }

    private static async Task AttachCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }
}

