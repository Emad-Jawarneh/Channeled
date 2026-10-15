using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Clipboard;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ClipboardPasteApiTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateChannelAsync("files", ChannelVisibility.Everyone, ChannelKind.File);
        await CreateChannelAsync("other", ChannelVisibility.Everyone, ChannelKind.File);
    }

    [Fact]
    public async Task Same_folder_copy_uses_copy_name()
    {
        var file = await UploadChannelFileAsync("note.txt", "hello");
        var body = new ClipboardPasteRequest(
            "copy",
            [file.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);
        result.Results[0].NewItemId.Should().NotBeNull();

        var list = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items");
        list!.Items.Should().Contain(i => i.Name == "note (copy).txt");
    }

    [Fact]
    public async Task Paste_folder_into_descendant_is_rejected_per_item()
    {
        var parent = await CreateFolderAsync("A", null);
        var child = await CreateFolderAsync("B", parent.Id);

        var body = new ClipboardPasteRequest(
            "copy",
            [parent.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", child.Id));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Failed.Should().Be(1);
        result.Results[0].ErrorCode.Should().Be("validation_error");
    }

    [Fact]
    public async Task Copy_folder_into_another_folder_succeeds()
    {
        var src = await CreateFolderAsync("CopyMe", null);
        var dest = await CreateFolderAsync("PasteHere", null);
        var body = new ClipboardPasteRequest(
            "copy",
            [src.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", dest.Id));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Results[0].NewItemId.Should().NotBeNull().And.NotBe(src.Id);

        var list = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items?folderId={dest.Id}");
        list!.Items.Should().Contain(i => i.Id == result.Results[0].NewItemId && i.Name == "CopyMe");
    }

    [Fact]
    public async Task Copy_nested_folder_tree_succeeds()
    {
        var src = await CreateFolderAsync("TreeRoot", null);
        var child = await CreateFolderAsync("TreeChild", src.Id);
        var dest = await CreateFolderAsync("TreeDest", null);
        var body = new ClipboardPasteRequest(
            "copy",
            [src.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", dest.Id));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);
        result.Failed.Should().Be(0);

        var copiedRootId = result.Results[0].NewItemId!;
        var underDest = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items?folderId={dest.Id}");
        underDest!.Items.Should().Contain(i => i.Id == copiedRootId && i.Name == "TreeRoot");

        var underCopy = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items?folderId={copiedRootId}");
        underCopy!.Items.Should().Contain(i => i.Name == "TreeChild" && i.Id != child.Id);
    }

    [Fact]
    public async Task Cut_folder_into_another_folder_succeeds()
    {
        var src = await CreateFolderAsync("MoveMeFolder", null);
        var dest = await CreateFolderAsync("MoveDest", null);
        var body = new ClipboardPasteRequest(
            "cut",
            [src.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", dest.Id));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);
        result.Failed.Should().Be(0);

        var root = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items");
        root!.Items.Should().NotContain(i => i.Id == src.Id);

        var inside = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items?folderId={dest.Id}");
        inside!.Items.Should().Contain(i => i.Id == src.Id && i.Name == "MoveMeFolder");
    }

    [Fact]
    public async Task Over_50_items_returns_validation_error()
    {
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var body = new ClipboardPasteRequest(
            "copy",
            ids,
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "files", null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("validation_error");
    }

    [Fact]
    public async Task Cut_folder_cross_channel_is_rejected()
    {
        var folder = await CreateFolderAsync("MoveMe", null);
        var body = new ClipboardPasteRequest(
            "cut",
            [folder.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "other", null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Failed.Should().Be(1);
        result.Results[0].ErrorCode.Should().Be("validation_error");
    }

    [Fact]
    public async Task Cut_folder_personal_to_channel_is_rejected()
    {
        var folderResp = await Owner.Client.PostAsJsonAsync(
            "/api/v1/me/files/folders",
            new CreateFolderRequest("PersonalFolder", null));
        folderResp.EnsureSuccessStatusCode();
        var folder = (await folderResp.Content.ReadFromJsonAsync<FileItemDto>())!;

        var body = new ClipboardPasteRequest(
            "cut",
            [folder.Id],
            Scope("personal", null, null, null),
            Scope("channel", Slug, "files", null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Failed.Should().Be(1);
        result.Results[0].ErrorCode.Should().Be("validation_error");
        result.Results[0].Message.Should().Contain("Copy them instead");
    }

    [Fact]
    public async Task Cut_team_to_personal_is_rejected()
    {
        var file = await UploadChannelFileAsync("team.txt", "x");
        var body = new ClipboardPasteRequest(
            "cut",
            [file.Id],
            Scope("channel", Slug, "files", null),
            Scope("personal", null, null, null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Failed.Should().Be(1);
        result.Results[0].ErrorCode.Should().Be("validation_error");
    }

    [Fact]
    public async Task Permission_denied_on_paste_to_readonly_channel()
    {
        var member = await NewMemberAsync("Writer", MemberRole.Member);
        var file = await UploadChannelFileAsync("secret.txt", "data", member);

        var restricted = await CreateChannelAsync("locked", ChannelVisibility.Restricted, ChannelKind.File);
        await GrantAsync("locked", member, readOnly: true);

        var body = new ClipboardPasteRequest(
            "copy",
            [file.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "locked", null));

        var resp = await member.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Failed.Should().Be(1);
        result.Results[0].ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Copy_channel_to_personal_succeeds()
    {
        var file = await UploadChannelFileAsync("from-team.txt", "bytes");
        var body = new ClipboardPasteRequest(
            "copy",
            [file.Id],
            Scope("channel", Slug, "files", null),
            Scope("personal", null, null, null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);
        result.Results[0].NewItemId.Should().NotBeNull();

        var list = await Owner.Client.GetFromJsonAsync<ItemListResponse>("/api/v1/me/files/items");
        list!.Items.Should().Contain(i => i.Id == result.Results[0].NewItemId);
    }

    [Fact]
    public async Task Cut_file_cross_channel_succeeds()
    {
        var file = await UploadChannelFileAsync("move-me.txt", "go");
        var body = new ClipboardPasteRequest(
            "cut",
            [file.Id],
            Scope("channel", Slug, "files", null),
            Scope("channel", Slug, "other", null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ClipboardPasteResponse>();
        result!.Succeeded.Should().Be(1);

        var source = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/files/items");
        source!.Items.Should().NotContain(i => i.Id == file.Id);

        var target = await Owner.Client.GetFromJsonAsync<ItemListResponse>(
            $"/api/v1/teams/{Slug}/channels/other/items");
        target!.Items.Should().Contain(i => i.Id == file.Id && i.Name == "move-me.txt");
    }

    [Fact]
    public async Task Quota_exceeded_fails_before_writes()
    {
        var file = await UploadPersonalFileAsync("mine.txt", "payload");
        await DbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == "owner@example.com");
            user.PersonalQuotaBytes = user.PersonalUsageBytes;
            await db.SaveChangesAsync();
        });

        var body = new ClipboardPasteRequest(
            "copy",
            [file.Id],
            Scope("personal", null, null, null),
            Scope("personal", null, null, null));

        var resp = await Owner.Client.PostAsJsonAsync("/api/v1/clipboard/paste", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("personal_quota_exceeded");

        var list = await Owner.Client.GetFromJsonAsync<ItemListResponse>("/api/v1/me/files/items");
        list!.Items.Count(i => i.Name.Contains("(copy)", StringComparison.Ordinal)).Should().Be(0);
    }

    private static ClipboardScopeRequest Scope(string scope, string? slug, string? channel, Guid? folderId) =>
        new(scope, slug, channel, folderId);

    private async Task<FileItemDto> CreateFolderAsync(string name, Guid? parentId)
    {
        var resp = await Owner.Client.PostAsJsonAsync(
            $"/api/v1/teams/{Slug}/channels/files/folders",
            new CreateFolderRequest(name, parentId));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<FileItemDto>())!;
    }

    private async Task<FileItemDto> UploadChannelFileAsync(string name, string text, Person? uploader = null)
    {
        var client = uploader?.Client ?? Owner.Client;
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var startResp = await client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            Slug, "files", name, payload.Length, "text/plain", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();
        using (var partContent = new ByteArrayContent(payload))
        {
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", partContent)).EnsureSuccessStatusCode();
        }

        var complete = await client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, Guid.NewGuid().ToString("N")));
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<FileItemDto>())!;
    }

    private async Task<FileItemDto> UploadPersonalFileAsync(string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var startResp = await Owner.Client.PostAsJsonAsync("/api/v1/me/files/uploads", new Channeled.Application.Personal.StartPersonalUploadRequest(
            name, payload.Length, "text/plain", null));
        startResp.EnsureSuccessStatusCode();
        var start = await startResp.Content.ReadFromJsonAsync<StartUploadResponse>();
        using (var partContent = new ByteArrayContent(payload))
        {
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await Owner.Client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", partContent)).EnsureSuccessStatusCode();
        }

        var complete = await Owner.Client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, Guid.NewGuid().ToString("N")));
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<FileItemDto>())!;
    }
}
