using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class SearchAndQuotaTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _client = null!;
    private string _slug = "";

    public SearchAndQuotaTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("owner@example.com", "Owner", "OwnerPassword1!"));

        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        (await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@example.com", "OwnerPassword1!")))
            .EnsureSuccessStatusCode();
        var csrf = await _client.GetAsync("/api/v1/auth/csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        var team = await (await _client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("SearchCo", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        (await _client.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Open))).EnsureSuccessStatusCode();

        await UploadAsync("alpha-report.pdf", "hello");
        await UploadAsync("beta-notes.txt", "world");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Search_channel_scope_finds_by_name_and_type_filter()
    {
        var resp = await _client.GetAsync(
            $"/api/v1/search?q=report&scope=channel&teamSlug={_slug}&channelName=general");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<SearchResponse>();
        body!.Hits.Should().Contain(h => h.Name.Contains("report", StringComparison.OrdinalIgnoreCase));

        var filtered = await _client.GetAsync(
            $"/api/v1/search?q=beta&scope=channel&teamSlug={_slug}&channelName=general&type=text");
        filtered.EnsureSuccessStatusCode();
        var filteredBody = await filtered.Content.ReadFromJsonAsync<SearchResponse>();
        filteredBody!.Hits.Should().Contain(h => h.Name.Contains("beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Team_storage_reports_usage()
    {
        var resp = await _client.GetAsync($"/api/v1/teams/{_slug}/storage");
        resp.EnsureSuccessStatusCode();
        var storage = await resp.Content.ReadFromJsonAsync<TeamStorageDto>();
        storage!.UsageBytes.Should().BeGreaterThan(0);
        storage.QuotaBytes.Should().BeGreaterThan(storage.UsageBytes);
    }

    [Fact]
    public async Task Upload_returns_team_quota_exceeded_when_over_quota()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var team = await db.Teams.FirstAsync(t => t.Slug == _slug);
        team.QuotaBytes = team.UsageBytes;
        await db.SaveChangesAsync();

        var start = await _client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, "general", "overflow.bin", 10, "application/octet-stream", null));
        start.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("team_quota_exceeded");
    }

    [Fact]
    public async Task Quota_reconciliation_fixes_drifted_usage_counter()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var team = await db.Teams.FirstAsync(t => t.Slug == _slug);
        var correct = team.UsageBytes;
        team.UsageBytes = 1;
        await db.SaveChangesAsync();

        var reconcile = scope.ServiceProvider.GetRequiredService<IQuotaReconciliationService>();
        await reconcile.ReconcileAsync();

        await db.Entry(team).ReloadAsync();
        team.UsageBytes.Should().Be(correct);
    }

    private async Task UploadAsync(string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var startResp = await _client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _slug, "general", name, payload.Length, "application/octet-stream", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await _client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null))).EnsureSuccessStatusCode();
    }
}
