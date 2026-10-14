using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Chat;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

/// <summary>
/// One seeded team shared by a whole test class (logins are rate limited at 20/min per host, so users are
/// created and logged in once). Tests create their own channels to stay independent.
/// </summary>
public sealed class ChatWorld : IAsyncDisposable
{
    public const string Password = "ChatPassw0rd!x";

    private readonly AuthApiFactory _factory;
    private int _channelSeq;

    private ChatWorld(AuthApiFactory factory) => _factory = factory;

    public AuthApiFactory Factory => _factory;
    public string Slug { get; private set; } = "";
    public Guid TeamId { get; private set; }

    public Actor Owner { get; private set; } = null!;
    public Actor Admin { get; private set; } = null!;
    public Actor Member { get; private set; } = null!;
    public Actor Viewer { get; private set; } = null!;
    public Actor Outsider { get; private set; } = null!;

    public sealed record Actor(string Email, Guid Id, HttpClient Client, string Cookie);

    public static async Task<ChatWorld> CreateAsync(AuthApiFactory factory)
    {
        await factory.ResetDatabaseAsync();
        var world = new ChatWorld(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.SeedOperatorAsync(new SeedOperatorRequest("chat-owner@example.com", "Chat Owner", Password));

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var (email, name) in new[]
                     {
                         ("chat-admin@example.com", "Chat Admin"),
                         ("chat-member@example.com", "Chat Member"),
                         ("chat-viewer@example.com", "Chat Viewer"),
                         ("chat-outsider@example.com", "Chat Outsider"),
                     })
            {
                var user = new AppUser
                {
                    Id = UuidV7.NewGuid(),
                    Email = email,
                    UserName = email,
                    DisplayName = name,
                    UsernameSlug = email.Split('@')[0].Replace(".", "")[..30],
                    EmailConfirmed = true,
                    Status = "active",
                };
                (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            }
        }

        world.Owner = await world.LoginAsync("chat-owner@example.com");
        world.Admin = await world.LoginAsync("chat-admin@example.com");
        world.Member = await world.LoginAsync("chat-member@example.com");
        world.Viewer = await world.LoginAsync("chat-viewer@example.com");
        world.Outsider = await world.LoginAsync("chat-outsider@example.com");

        var teamResponse = await world.Owner.Client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Chat Team", null));
        teamResponse.EnsureSuccessStatusCode();
        var team = await teamResponse.Content.ReadFromJsonAsync<TeamDto>();
        world.Slug = team!.Slug;
        world.TeamId = team.Id;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamMembers.AddRange(
                new TeamMemberEntity { TeamId = team.Id, UserId = world.Admin.Id, Role = MemberRole.Admin, JoinedAt = DateTimeOffset.UtcNow },
                new TeamMemberEntity { TeamId = team.Id, UserId = world.Member.Id, Role = MemberRole.Member, JoinedAt = DateTimeOffset.UtcNow },
                new TeamMemberEntity { TeamId = team.Id, UserId = world.Viewer.Id, Role = MemberRole.Viewer, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        return world;
    }

    public async Task<ChannelDto> CreateChannelAsync(
        ChannelKind kind,
        ChannelVisibility visibility = ChannelVisibility.Everyone,
        string? prefix = null)
    {
        var name = $"{prefix ?? (kind == ChannelKind.Text ? "talk" : "files")}-{Interlocked.Increment(ref _channelSeq)}";
        var response = await Owner.Client.PostAsJsonAsync(
            $"/api/v1/teams/{Slug}/channels",
            new CreateChannelRequest(name, null, visibility, kind));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelDto>())!;
    }

    public async Task SetChannelMemberAsync(ChannelDto channel, Actor user, bool canWrite)
    {
        var response = await Owner.Client.PutAsJsonAsync(
            $"/api/v1/teams/{Slug}/channels/{channel.Name}/members",
            new UpsertChannelMemberRequest(user.Id, ReadOnly: !canWrite));
        response.EnsureSuccessStatusCode();
    }

    public Task<HttpResponseMessage> PostAsync(Actor actor, ChannelDto channel, string body) =>
        actor.Client.PostAsJsonAsync($"/api/v1/teams/{Slug}/channels/{channel.Name}/messages", new PostMessageRequest(body));

    public Task<HttpResponseMessage> ListAsync(Actor actor, ChannelDto channel, string? query = null) =>
        actor.Client.GetAsync($"/api/v1/teams/{Slug}/channels/{channel.Name}/messages{(query is null ? "" : "?" + query)}");

    public Task<HttpResponseMessage> DeleteAsync(Actor actor, ChannelDto channel, Guid messageId) =>
        actor.Client.DeleteAsync($"/api/v1/teams/{Slug}/channels/{channel.Name}/messages/{messageId}");

    public async Task<ChannelMessageDto> PostOkAsync(Actor actor, ChannelDto channel, string body)
    {
        var response = await PostAsync(actor, channel, body);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ChannelMessageDto>())!;
    }

    public async Task<MessageListResponse> ListOkAsync(Actor actor, ChannelDto channel, string? query = null)
    {
        var response = await ListAsync(actor, channel, query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageListResponse>())!;
    }

    public static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString()!;
    }

    public async Task<Actor> LoginAsync(string email)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0]));

        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var id = (await users.FindByEmailAsync(email))!.Id;
        return new Actor(email, id, client, cookie);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var actor in new[] { Owner, Admin, Member, Viewer, Outsider })
        {
            actor?.Client.Dispose();
        }

        await _factory.DisposeAsync();
    }
}
