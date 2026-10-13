using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class PersonalIsolationTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private HttpClient _other = null!;
    private HttpClient _admin = null!;
    private string _teamSlug = "";
    private Guid _personalFileId;

    public PersonalIsolationTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("admin@example.com", "Admin", "AdminPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var (email, name, pass) in new[]
                 {
                     ("owner@example.com", "Owner", "OwnerPassword1!"),
                     ("other@example.com", "Other", "OtherPassword1!"),
                 })
        {
            var user = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = name,
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(user, pass)).Succeeded.Should().BeTrue();
        }

        _admin = CreateClient();
        _owner = CreateClient();
        _other = CreateClient();

        await LoginAsync(_admin, "admin@example.com", "AdminPassword1!");
        await LoginAsync(_owner, "owner@example.com", "OwnerPassword1!");
        await LoginAsync(_other, "other@example.com", "OtherPassword1!");

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("PersonalTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _teamSlug = team!.Slug;
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_teamSlug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        _personalFileId = await UploadPersonalAsync(_owner, "secret.txt", "top-secret-bytes");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Other_user_cannot_read_personal_item_or_content()
    {
        var item = await _other.GetAsync($"/api/v1/me/files/items/{_personalFileId}");
        item.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await _other.GetAsync($"/api/v1/me/files/items/{_personalFileId}/content");
        content.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await _other.GetAsync("/api/v1/me/files/items");
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadFromJsonAsync<ItemListResponse>();
        body!.Items.Should().NotContain(i => i.Id == _personalFileId);
    }

    [Fact]
    public async Task Platform_admin_cannot_reach_another_users_personal_content()
    {
        // Confirm admin flag
        var me = await _admin.GetAsync("/api/v1/auth/me");
        me.EnsureSuccessStatusCode();
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        meBody.GetProperty("isPlatformAdmin").GetBoolean().Should().BeTrue();

        var item = await _admin.GetAsync($"/api/v1/me/files/items/{_personalFileId}");
        item.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await _admin.GetAsync($"/api/v1/me/files/items/{_personalFileId}/content");
        content.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var star = await _admin.PostAsync($"/api/v1/me/files/items/{_personalFileId}/star", null);
        star.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var copy = await _admin.PostAsJsonAsync(
            $"/api/v1/me/files/items/{_personalFileId}/copy-to-channel",
            new CopyToChannelRequest(_teamSlug, "general", null, null));
        copy.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_can_star_recent_and_copy_to_channel_as_independent_object()
    {
        (await _owner.PostAsync($"/api/v1/me/files/items/{_personalFileId}/star", null)).EnsureSuccessStatusCode();

        var starred = await _owner.GetAsync("/api/v1/me/files/starred");
        starred.EnsureSuccessStatusCode();
        (await starred.Content.ReadFromJsonAsync<ItemListResponse>())!.Items
            .Should().Contain(i => i.Id == _personalFileId && i.Starred);

        var recent = await _owner.GetAsync("/api/v1/me/files/recent");
        recent.EnsureSuccessStatusCode();
        (await recent.Content.ReadFromJsonAsync<ItemListResponse>())!.Items
            .Should().Contain(i => i.Id == _personalFileId);

        var copyResp = await _owner.PostAsJsonAsync(
            $"/api/v1/me/files/items/{_personalFileId}/copy-to-channel",
            new CopyToChannelRequest(_teamSlug, "general", null, "copied.txt"));
        copyResp.EnsureSuccessStatusCode();
        var copied = await copyResp.Content.ReadFromJsonAsync<FileItemDto>();
        copied!.Id.Should().NotBe(_personalFileId);
        copied.ChannelId.Should().NotBeNull();
        copied.OwnerUserId.Should().BeNull();
        copied.Name.Should().Be("copied.txt");

        var channelItems = await _owner.GetAsync($"/api/v1/teams/{_teamSlug}/channels/general/items");
        channelItems.EnsureSuccessStatusCode();
        (await channelItems.Content.ReadFromJsonAsync<ItemListResponse>())!.Items
            .Should().Contain(i => i.Id == copied.Id);

        // Original still personal
        var original = await _owner.GetAsync($"/api/v1/me/files/items/{_personalFileId}");
        original.EnsureSuccessStatusCode();
        var origDto = await original.Content.ReadFromJsonAsync<FileItemDto>();
        origDto!.OwnerUserId.Should().NotBeNull();
        origDto.ChannelId.Should().BeNull();
    }

    [Fact]
    public async Task Personal_quota_blocks_oversized_upload()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByEmailAsync("owner@example.com");
        owner!.PersonalQuotaBytes = 10;
        owner.PersonalUsageBytes = 0;
        await users.UpdateAsync(owner);

        var start = await _owner.PostAsJsonAsync(
            "/api/v1/me/files/uploads",
            new StartPersonalUploadRequest("big.bin", 100, "application/octet-stream", null));
        start.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("personal_quota_exceeded");
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password)))
            .EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<Guid> UploadPersonalAsync(HttpClient client, string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var startResp = await client.PostAsJsonAsync(
            "/api/v1/me/files/uploads",
            new StartPersonalUploadRequest(name, payload.Length, "text/plain", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();

        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();

        using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/uploads/{start.UploadId}/complete")
        {
            Content = JsonContent.Create(new CompleteUploadRequest(sha, null)),
        };
        var complete = await client.SendAsync(completeReq);
        complete.EnsureSuccessStatusCode();
        var file = await complete.Content.ReadFromJsonAsync<FileItemDto>();
        return file!.Id;
    }
}
