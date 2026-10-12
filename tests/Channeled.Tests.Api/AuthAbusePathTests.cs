using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Identity;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

public class AuthAbusePathTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _client = null!;

    public AuthAbusePathTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        await SeedOperatorAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_happy_path_sets_cookie_and_me_works()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "OperatorPass1!"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await _client.GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<AuthUserDto>();
        body!.Email.Should().Be("admin@example.com");
        body.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Lockout_after_five_failures()
    {
        for (var i = 0; i < 5; i++)
        {
            var failed = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "wrong-password!!"));
            failed.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Locked);
        }

        var locked = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "OperatorPass1!"));
        locked.StatusCode.Should().Be(HttpStatusCode.Locked);
        var problem = await locked.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("locked_out");
    }

    [Fact]
    public async Task Invitation_reject_expired_and_reused_tokens()
    {
        await LoginAsAdminAsync();
        await EnsureCsrfAsync();

        var invite = await _client.PostAsJsonAsync(
            "/api/v1/auth/invitations",
            new CreateInvitationRequest("member@example.com", null));
        invite.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await invite.Content.ReadFromJsonAsync<InvitationCreatedDto>();
        created.Should().NotBeNull();

        var accept = await _client.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptInvitationRequest(created!.RawToken, "Member", "MemberPassword1"));
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await _client.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptInvitationRequest(created.RawToken, "Other", "MemberPassword1"));
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_token");

        var expiredInvite = await _client.PostAsJsonAsync(
            "/api/v1/auth/invitations",
            new CreateInvitationRequest("expired@example.com", null));
        expiredInvite.EnsureSuccessStatusCode();
        var expiredCreated = await expiredInvite.Content.ReadFromJsonAsync<InvitationCreatedDto>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.PlatformInvitations.FirstAsync(i => i.Id == expiredCreated!.InvitationId);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var expiredAccept = await _client.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptInvitationRequest(expiredCreated!.RawToken, "Expired", "MemberPassword1"));
        expiredAccept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await expiredAccept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_token");
    }

    [Fact]
    public async Task Password_reset_rejects_reused_token()
    {
        var forgot = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("admin@example.com"));
        forgot.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await forgot.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("debugToken").GetString();
        token.Should().NotBeNullOrEmpty();

        var reset = await _client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest(token!, "NewOperatorPass1"));
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await _client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest(token!, "AnotherPass1234"));
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("invalid_token");
    }

    [Fact]
    public async Task Mutating_request_without_csrf_is_rejected()
    {
        await LoginAsAdminAsync();

        // No CSRF header on logout
        var logout = await _client.PostAsync("/api/v1/auth/logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await logout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("csrf_rejected");
    }

    private async Task SeedOperatorAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var result = await auth.SeedOperatorAsync(new SeedOperatorRequest("admin@example.com", "Admin", "OperatorPass1!"));
        result.Succeeded.Should().BeTrue(result.Message);
    }

    private async Task LoginAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@example.com", "OperatorPass1!"));
        login.EnsureSuccessStatusCode();
    }

    private async Task EnsureCsrfAsync()
    {
        var csrf = await _client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        _client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }
}
