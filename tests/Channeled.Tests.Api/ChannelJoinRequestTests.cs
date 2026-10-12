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
public class ChannelJoinRequestTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private string _slug = "";

    public ChannelJoinRequestTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("admin@example.com", "Admin", "AdminPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var member = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = "member@example.com",
            UserName = "member@example.com",
            DisplayName = "Member",
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(member, "MemberPassword1!")).Succeeded.Should().BeTrue();

        _admin = CreateClient();
        _member = CreateClient();
        (await _admin.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "AdminPassword1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_admin);
        (await _member.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("member@example.com", "MemberPassword1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_member);

        var team = await (await _admin.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("JoinTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;

        var invite = await (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/invitations",
            new CreateTeamInvitationRequest("member@example.com", MemberRole.Member)))
            .Content.ReadFromJsonAsync<TeamInvitationDto>();
        (await _member.PostAsJsonAsync(
            "/api/v1/teams/invitations/accept",
            new AcceptTeamInvitationRequest(invite!.RawToken!))).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("secret", null, ChannelVisibility.Private, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Member_requests_join_admin_approves()
    {
        var info = await _member.GetAsync($"/api/v1/teams/{_slug}/channels/secret/join");
        info.StatusCode.Should().Be(HttpStatusCode.OK);
        var joinInfo = await info.Content.ReadFromJsonAsync<ChannelJoinInfoDto>();
        joinInfo!.HasAccess.Should().BeFalse();
        joinInfo.JoinPath.Should().Be($"/join/{_slug}/secret");

        var created = await _member.PostAsync($"/api/v1/teams/{_slug}/channels/secret/join-requests", null);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = await created.Content.ReadFromJsonAsync<ChannelJoinRequestDto>();
        request!.Status.Should().Be(ChannelJoinRequestStatus.Pending);

        var listed = await _admin.GetAsync($"/api/v1/teams/{_slug}/channels/secret/join-requests");
        listed.EnsureSuccessStatusCode();
        var pending = await listed.Content.ReadFromJsonAsync<List<ChannelJoinRequestDto>>();
        pending!.Should().ContainSingle(r => r.Id == request.Id);

        (await _admin.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/secret/join-requests/{request.Id}/approve",
            new ApproveChannelJoinRequestRequest())).EnsureSuccessStatusCode();

        var after = await _member.GetAsync($"/api/v1/teams/{_slug}/channels/secret/join");
        (await after.Content.ReadFromJsonAsync<ChannelJoinInfoDto>())!.HasAccess.Should().BeTrue();

        var channel = await _member.GetAsync($"/api/v1/teams/{_slug}/channels/secret");
        channel.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_rejects_join_request()
    {
        var created = await _member.PostAsync($"/api/v1/teams/{_slug}/channels/secret/join-requests", null);
        created.EnsureSuccessStatusCode();
        var request = await created.Content.ReadFromJsonAsync<ChannelJoinRequestDto>();

        (await _admin.PostAsync(
            $"/api/v1/teams/{_slug}/channels/secret/join-requests/{request!.Id}/reject",
            null)).EnsureSuccessStatusCode();

        var listed = await _admin.GetAsync($"/api/v1/teams/{_slug}/channels/secret/join-requests");
        var pending = await listed.Content.ReadFromJsonAsync<List<ChannelJoinRequestDto>>();
        pending!.Should().BeEmpty();

        var channel = await _member.GetAsync($"/api/v1/teams/{_slug}/channels/secret");
        channel.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Member_cannot_list_join_requests_without_manage()
    {
        var listed = await _member.GetAsync($"/api/v1/teams/{_slug}/channels/secret/join-requests");
        listed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("not_found");
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
