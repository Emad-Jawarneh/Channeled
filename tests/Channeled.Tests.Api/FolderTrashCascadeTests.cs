using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class FolderTrashCascadeTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _owner = null!;
    private string _slug = "";

    public FolderTrashCascadeTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        await auth.SeedOperatorAsync(new SeedOperatorRequest("ft-owner@example.com", "Owner", "OwnerPassword1!"));

        _owner = CreateClient();
        await LoginAsync(_owner, "ft-owner@example.com", "OwnerPassword1!");
        var team = await (await _owner.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Folder Trash", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        _slug = team!.Slug;
        (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Folder_trash_cascades_batch_and_restore_leaves_independent_trash()
    {
        var parent = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("parent", null))).Content.ReadFromJsonAsync<FileItemDto>();
        var child = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("child", parent!.Id))).Content.ReadFromJsonAsync<FileItemDto>();

        // Independently trash a nested folder first (its own batch).
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{child!.Id}"))
            .EnsureSuccessStatusCode();

        Guid? childBatch;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Folders.SingleAsync(f => f.Id == child.Id);
            row.DeletedAt.Should().NotBeNull();
            childBatch = row.DeletedBatchId;
            childBatch.Should().NotBeNull();
        }

        // Trash parent — should cascade only live descendants (child already trashed).
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{parent.Id}"))
            .EnsureSuccessStatusCode();

        Guid? parentBatch;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Folders.SingleAsync(f => f.Id == parent.Id);
            parentBatch = p.DeletedBatchId;
            parentBatch.Should().NotBe(childBatch!.Value);

            var c = await db.Folders.SingleAsync(f => f.Id == child.Id);
            c.DeletedBatchId.Should().Be(childBatch);
        }

        // Restore parent batch — child stays trashed.
        (await _owner.PostAsync($"/api/v1/teams/{_slug}/channels/general/items/{parent.Id}/restore", null))
            .EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Folders.SingleAsync(f => f.Id == parent.Id)).DeletedAt.Should().BeNull();
            (await db.Folders.SingleAsync(f => f.Id == child.Id)).DeletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Nested_restore_blocked_when_parent_trashed()
    {
        var parent = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("p2", null))).Content.ReadFromJsonAsync<FileItemDto>();
        var child = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("c2", parent!.Id))).Content.ReadFromJsonAsync<FileItemDto>();

        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{child!.Id}"))
            .EnsureSuccessStatusCode();
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{parent.Id}"))
            .EnsureSuccessStatusCode();

        // Child was independently trashed before parent; still under trashed parent path for restore of child alone after we... 
        // Actually after parent trash, child's ancestor is trashed. Restore child should fail.
        var resp = await _owner.PostAsync(
            $"/api/v1/teams/{_slug}/channels/general/items/{child.Id}/restore", null);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.ParentTrashed);
    }

    [Fact]
    public async Task Purge_batch_is_idempotent_after_crash_marker()
    {
        var folder = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("purge-me", null))).Content.ReadFromJsonAsync<FileItemDto>();
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{folder!.Id}"))
            .EnsureSuccessStatusCode();

        Guid batchId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Folders.SingleAsync(f => f.Id == folder.Id);
            batchId = row.DeletedBatchId!.Value;
            await db.TrashBatches.Where(b => b.Id == batchId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.DeletedAt, DateTimeOffset.UtcNow.AddDays(-60)));
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var purge = scope.ServiceProvider.GetRequiredService<ITrashPurgeService>();
            (await purge.PurgeBatchAsync(batchId)).Should().BeTrue();
            (await purge.PurgeBatchAsync(batchId)).Should().BeFalse();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Folders.AnyAsync(f => f.Id == folder.Id)).Should().BeFalse();
            (await db.TrashBatches.AnyAsync(b => b.Id == batchId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Purge_retry_after_mid_batch_row_loss_leaves_zero_orphans()
    {
        var parent = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("mid-parent", null))).Content.ReadFromJsonAsync<FileItemDto>();
        var child = await (await _owner.PostAsJsonAsync(
            $"/api/v1/teams/{_slug}/channels/general/folders",
            new CreateFolderRequest("mid-child", parent!.Id))).Content.ReadFromJsonAsync<FileItemDto>();
        (await _owner.DeleteAsync($"/api/v1/teams/{_slug}/channels/general/items/{parent.Id}"))
            .EnsureSuccessStatusCode();

        Guid batchId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            batchId = (await db.Folders.SingleAsync(f => f.Id == parent.Id)).DeletedBatchId!.Value;
            // Simulate crash after child row removed but before parent/batch cleared.
            await db.Folders.Where(f => f.Id == child!.Id).ExecuteDeleteAsync();
            await db.TrashBatches.Where(b => b.Id == batchId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.State, "purging")
                .SetProperty(b => b.DeletedAt, DateTimeOffset.UtcNow.AddDays(-60)));
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var purge = scope.ServiceProvider.GetRequiredService<ITrashPurgeService>();
            (await purge.PurgeBatchAsync(batchId)).Should().BeTrue();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Folders.AnyAsync(f => f.Id == parent.Id || f.Id == child!.Id)).Should().BeFalse();
            (await db.TrashBatches.AnyAsync(b => b.Id == batchId)).Should().BeFalse();
            var reconcile = scope.ServiceProvider.GetRequiredService<Channeled.Application.Abstractions.Operations.IStoreReconciliationService>();
            (await reconcile.ReconcileAsync()).IsClean.Should().BeTrue();
        }
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
