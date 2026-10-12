using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class FilesApiTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private HttpClient _outsider = null!;
    private string _slug = "";

    public FilesApiTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));

        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Channeled.Infrastructure.Identity.AppUser>>();
        var outsider = new Channeled.Infrastructure.Identity.AppUser
        {
            Id = Channeled.Domain.Identity.UuidV7.NewGuid(),
            Email = "outsider@example.com",
            UserName = "outsider@example.com",
            DisplayName = "Outsider",
            EmailConfirmed = true,
            Status = "active",
        };
        (await users.CreateAsync(outsider, "OutsiderPass1!")).Succeeded.Should().BeTrue();

        _owner = CreateClient();
        _outsider = CreateClient();
        (await _owner.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@example.com", "OwnerPassword1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_owner);
        (await _outsider.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("outsider@example.com", "OutsiderPass1!")))
            .EnsureSuccessStatusCode();
        await AttachCsrfAsync(_outsider);

        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Files", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Open, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Outsider_gets_404_for_channel_items()
    {
        var response = await _outsider.GetAsync($"/api/v1/teams/{_slug}/channels/general/items");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_download_rename_trash_restore_round_trip()
    {
        var payload = Encoding.UTF8.GetBytes("hello-channeled-m3-" + Guid.NewGuid());
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var startResp = await _owner.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, "general", "note.txt", payload.Length, "text/plain", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();

        using (var partContent = new ByteArrayContent(payload))
        {
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var put = await _owner.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", partContent);
            put.EnsureSuccessStatusCode();
        }

        var parts = await _owner.GetAsync($"/api/v1/uploads/{start.UploadId}/parts");
        parts.EnsureSuccessStatusCode();

        using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/uploads/{start.UploadId}/complete")
        {
            Content = JsonContent.Create(new CompleteUploadRequest(sha, "idem-1")),
        };
        completeReq.Headers.Add("Idempotency-Key", "idem-1");
        var complete = await _owner.SendAsync(completeReq);
        complete.EnsureSuccessStatusCode();
        var file = await complete.Content.ReadFromJsonAsync<FileItemDto>();
        file!.Sha256.Should().Be(sha);
        file.SizeBytes.Should().Be(payload.Length);

        var download = await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/general/items/{file.Id}/content");
        download.EnsureSuccessStatusCode();
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(payload);

        using var rangeReq = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/teams/{_slug}/channels/general/items/{file.Id}/content");
        rangeReq.Headers.Range = new RangeHeaderValue(0, 4);
        var ranged = await _owner.SendAsync(rangeReq);
        ranged.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await ranged.Content.ReadAsByteArrayAsync()).Should().Equal(payload.AsSpan(0, 5).ToArray());

        var renamed = await _owner.PatchAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{file.Id}",
            new RenameItemRequest("renamed.txt"));
        renamed.EnsureSuccessStatusCode();

        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{file.Id}")).EnsureSuccessStatusCode();

        var trash = await _owner.GetAsync($"/api/v1/teams/{_slug}/channels/general/items?trash=true");
        trash.EnsureSuccessStatusCode();
        var trashList = await trash.Content.ReadFromJsonAsync<ItemListResponse>();
        trashList!.Items.Should().Contain(i => i.Id == file.Id);

        (await _owner.PostAsync($"/api/v1/teams/{_slug}/channels/general/items/{file.Id}/restore", null))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Name_conflict_returns_409()
    {
        var folder = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("Docs", null));
        folder.EnsureSuccessStatusCode();

        var again = await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("docs", null));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("name_conflict");
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
