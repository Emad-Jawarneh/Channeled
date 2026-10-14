using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Chat;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

/// <summary>
/// Per-test fresh database with one team (owner = platform operator, RequestToJoin, default role Viewer) and a join
/// link. Users are created on demand and join through the real join-link flow.
/// </summary>
public abstract class TeamPrivacyTestBase : IAsyncLifetime
{
    public const string UserPassword = "UserPassword1!";

    private readonly List<HttpClient> _clients = [];

    protected TeamPrivacyTestBase(AuthApiFactory factory) => Factory = factory;

    protected AuthApiFactory Factory { get; }
    protected Person Owner { get; private set; } = null!;
    protected string Slug { get; private set; } = "";
    protected Guid TeamId { get; private set; }
    protected string Token { get; private set; } = "";

    protected sealed record Person(Guid Id, string Name, HttpClient Client);

    public virtual async Task InitializeAsync()
    {
        await Factory.ResetDatabaseAsync();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));
        }

        Owner = await LoginAsync("owner@example.com", "Owner", "OwnerPassword1!");
        var created = await Owner.Client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("PrivacyTeam", null));
        created.EnsureSuccessStatusCode();
        var team = (await created.Content.ReadFromJsonAsync<TeamDto>())!;
        Slug = team.Slug;
        TeamId = team.Id;
        Token = (await Owner.Client.GetFromJsonAsync<TeamJoinLinkDto>($"/api/v1/teams/{Slug}/join-link"))!.Token;
    }

    public virtual Task DisposeAsync()
    {
        foreach (var c in _clients)
        {
            c.Dispose();
        }

        return Task.CompletedTask;
    }

    protected string TeamUrl(string tail = "") => $"/api/v1/teams/{Slug}{tail}";

    protected async Task<Person> NewUserAsync(string name)
    {
        var email = $"{name.ToLowerInvariant()}@example.com";
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = name,
                UsernameSlug = email.Split('@')[0].Replace(".", ""),
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(user, UserPassword)).Succeeded.Should().BeTrue();
        }

        return await LoginAsync(email, name, UserPassword);
    }

    /// <summary>Creates the user and takes them through join link → approval (Admin = Member approval then promotion).</summary>
    protected async Task<Person> NewMemberAsync(string name, MemberRole role)
    {
        var person = await NewUserAsync(name);
        var joined = await JoinAsync(person);
        joined.PendingRequestId.Should().NotBeNull();
        (await Owner.Client.PostAsJsonAsync(
                TeamUrl($"/join-requests/{joined.PendingRequestId}/approve"),
                new ApproveTeamJoinRequestRequest(role == MemberRole.Viewer ? MemberRole.Viewer : MemberRole.Member)))
            .EnsureSuccessStatusCode();
        if (role >= MemberRole.Admin)
        {
            await SetRoleAsync(person, role);
        }

        return person;
    }

    protected async Task<TeamJoinResultDto> JoinAsync(Person person, string? token = null)
    {
        var resp = await person.Client.PostAsync($"/api/v1/teams/join/{token ?? Token}", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TeamJoinResultDto>())!;
    }

    protected async Task SetRoleAsync(Person person, MemberRole role)
    {
        var resp = await Owner.Client.PutAsJsonAsync(TeamUrl($"/members/{person.Id}/role"), new ChangeMemberRoleRequest(role));
        resp.EnsureSuccessStatusCode();
    }

    protected async Task<ChannelDto> CreateChannelAsync(string name, ChannelVisibility visibility, ChannelKind kind = ChannelKind.File)
    {
        var resp = await Owner.Client.PostAsJsonAsync(TeamUrl("/channels"), new CreateChannelRequest(name, null, visibility, kind));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ChannelDto>())!;
    }

    protected async Task GrantAsync(string channel, Person person, bool readOnly)
    {
        var resp = await Owner.Client.PutAsJsonAsync(
            TeamUrl($"/channels/{channel}/members"),
            new UpsertChannelMemberRequest(person.Id, readOnly));
        resp.EnsureSuccessStatusCode();
    }

    protected async Task<SetChannelVisibilityResult> SetVisibilityAsync(string channel, ChannelVisibility visibility)
    {
        var resp = await Owner.Client.PutAsJsonAsync(
            TeamUrl($"/channels/{channel}/visibility"),
            new SetChannelVisibilityRequest(visibility));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SetChannelVisibilityResult>())!;
    }

    protected Task<HttpResponseMessage> PostMessageAsync(Person person, string channel, string body = "hello") =>
        person.Client.PostAsJsonAsync(TeamUrl($"/channels/{channel}/messages"), new PostMessageRequest(body));

    protected Task<HttpResponseMessage> GetChannelAsync(Person person, string channel) =>
        person.Client.GetAsync(TeamUrl($"/channels/{channel}"));

    protected static async Task<string> ErrorCodeAsync(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    protected async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await work(db);
    }

    protected Task DbAsync(Func<ApplicationDbContext, Task> work) =>
        DbAsync(async db =>
        {
            await work(db);
            return 0;
        });

    protected Task<int> CountAuditAsync(AuditEventType type, Guid? targetUser = null)
    {
        var target = targetUser?.ToString();
        return DbAsync(db => db.AuditEvents.CountAsync(a =>
            a.TeamId == TeamId && a.EventType == type && (target == null || a.TargetId == target)));
    }

    protected async Task<FileEntity> SeedFileAsync(Guid channelId, string name, Guid uploader)
    {
        var now = DateTimeOffset.UtcNow;
        var file = new FileEntity
        {
            Id = UuidV7.NewGuid(),
            ChannelId = channelId,
            Name = name,
            ContentType = "text/plain",
            SizeBytes = 1,
            Sha256 = new string('a', 64),
            StorageKey = "seed/" + Guid.NewGuid(),
            UploadedBy = uploader,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await DbAsync(async db =>
        {
            db.Files.Add(file);
            await db.SaveChangesAsync();
        });
        return file;
    }

    private async Task<Person> LoginAsync(string email, string name, string password)
    {
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        _clients.Add(client);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());

        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var id = (await users.FindByEmailAsync(email))!.Id;
        return new Person(id, name, client);
    }
}
