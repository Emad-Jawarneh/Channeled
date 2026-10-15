using System.Net;
using System.Net.Http.Json;
using Channeled.Application.Files;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static Channeled.Tests.Api.DownloadHelpers;

namespace Channeled.Tests.Api;

/// <summary>GET /api/v1/items/{id}/locate — the single "open this item" call (chips, search, deep links).</summary>
[Collection("Api")]
public class LocateItemApiTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateChannelAsync("general", ChannelVisibility.Everyone);
    }

    private static Task<HttpResponseMessage> LocateAsync(HttpClient client, Guid id) => client.GetAsync($"/api/v1/items/{id}/locate");

    private static async Task<ItemLocationDto> LocatedAsync(HttpClient client, Guid id)
    {
        var response = await LocateAsync(client, id);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ItemLocationDto>())!;
    }

    private async Task<FileItemDto> FolderAsync(string name, Guid? parent = null)
    {
        var response = await Owner.Client.PostAsJsonAsync(TeamUrl("/channels/general/folders"), new CreateFolderRequest(name, parent));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FileItemDto>())!;
    }

    [Fact]
    public async Task Personal_items_are_located_only_for_their_owner_never_for_others_or_platform_admins()
    {
        var member = await NewMemberAsync("Pia", MemberRole.Member);
        var stranger = await NewUserAsync("Stu");
        var mine = await UploadPersonalFileAsync(member.Client, "diary.txt", "dear diary"u8.ToArray());

        var located = await LocatedAsync(member.Client, mine.Id);
        located.Scope.Should().Be("personal");
        located.State.Should().Be("live");
        located.Item.Name.Should().Be("diary.txt");

        // Owner is the platform operator in this fixture: no admin bypass for personal space.
        (await LocateAsync(Owner.Client, mine.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LocateAsync(stranger.Client, mine.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Channel_items_follow_browse_rules()
    {
        var member = await NewMemberAsync("Cal", MemberRole.Member);
        var outsider = await NewUserAsync("Oz");
        await CreateChannelAsync("vault", ChannelVisibility.Restricted);
        var open = await UploadChannelFileAsync(Owner.Client, Slug, "general", "open.txt", "o"u8.ToArray());
        var secret = await UploadChannelFileAsync(Owner.Client, Slug, "vault", "secret.txt", "s"u8.ToArray());

        var located = await LocatedAsync(member.Client, open.Id);
        located.Scope.Should().Be("channel");
        located.TeamSlug.Should().Be(Slug);
        located.ChannelName.Should().Be("general");

        (await LocateAsync(outsider.Client, open.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LocateAsync(member.Client, secret.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await GrantAsync("vault", member, readOnly: true);
        (await LocatedAsync(member.Client, secret.Id)).ChannelName.Should().Be("vault");
        (await LocateAsync(member.Client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deep_items_report_the_full_folder_path_root_first()
    {
        Guid? parent = null;
        var chain = new List<FileItemDto>();
        for (var depth = 1; depth <= 8; depth++)
        {
            var folder = await FolderAsync($"level{depth}", parent);
            chain.Add(folder);
            parent = folder.Id;
        }

        var file = await UploadChannelFileInFolderAsync(chain[^1].Id, "deep.txt");
        var located = await LocatedAsync(Owner.Client, file.Id);
        located.State.Should().Be("live");
        located.FolderId.Should().Be(chain[^1].Id);
        located.FolderPath.Select(p => p.Name).Should().Equal(chain.Select(c => c.Name));

        var folderLocated = await LocatedAsync(Owner.Client, chain[3].Id);
        folderLocated.Item.Kind.Should().Be(Channeled.Domain.Files.ItemKind.Folder);
        folderLocated.FolderId.Should().Be(chain[2].Id);
        folderLocated.FolderPath.Should().HaveCount(3);
    }

    [Fact]
    public async Task Location_follows_moves()
    {
        var folder = await FolderAsync("Before");
        var file = await UploadChannelFileInFolderAsync(folder.Id, "moving.txt");
        (await LocatedAsync(Owner.Client, file.Id)).FolderId.Should().Be(folder.Id);

        (await Owner.Client.PostAsJsonAsync(TeamUrl($"/channels/general/items/{file.Id}/move"), new MoveItemRequest(null)))
            .EnsureSuccessStatusCode();
        var moved = await LocatedAsync(Owner.Client, file.Id);
        moved.FolderId.Should().BeNull();
        moved.FolderPath.Should().BeEmpty();
    }

    [Fact]
    public async Task Trashed_items_point_at_the_outermost_trashed_entry()
    {
        var outer = await FolderAsync("Outer");
        var inner = await FolderAsync("Inner", outer.Id);
        var file = await UploadChannelFileInFolderAsync(inner.Id, "inside.txt");
        var loose = await UploadChannelFileAsync(Owner.Client, Slug, "general", "loose.txt", "l"u8.ToArray());

        (await Owner.Client.DeleteAsync(TeamUrl($"/channels/general/items/{loose.Id}"))).EnsureSuccessStatusCode();
        var self = await LocatedAsync(Owner.Client, loose.Id);
        self.State.Should().Be("trashed");
        self.TrashEntryId.Should().Be(loose.Id);

        (await Owner.Client.DeleteAsync(TeamUrl($"/channels/general/items/{inner.Id}"))).EnsureSuccessStatusCode();
        (await LocatedAsync(Owner.Client, file.Id)).TrashEntryId.Should().Be(inner.Id);

        (await Owner.Client.DeleteAsync(TeamUrl($"/channels/general/items/{outer.Id}"))).EnsureSuccessStatusCode();
        var outermost = await LocatedAsync(Owner.Client, file.Id);
        outermost.State.Should().Be("trashed");
        outermost.TrashEntryId.Should().Be(outer.Id);
    }

    [Fact]
    public async Task Purged_items_are_not_found_and_archived_channels_still_locate()
    {
        var gone = await UploadChannelFileAsync(Owner.Client, Slug, "general", "gone.txt", "g"u8.ToArray());
        await DbAsync(db => db.Files.Where(f => f.Id == gone.Id).ExecuteDeleteAsync());
        (await LocateAsync(Owner.Client, gone.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await CreateChannelAsync("old", ChannelVisibility.Everyone);
        var kept = await UploadChannelFileAsync(Owner.Client, Slug, "old", "kept.txt", "k"u8.ToArray());
        (await Owner.Client.PostAsync(TeamUrl("/channels/old/archive"), null)).EnsureSuccessStatusCode();
        (await LocatedAsync(Owner.Client, kept.Id)).ChannelName.Should().Be("old");
    }

    private async Task<FileItemDto> UploadChannelFileInFolderAsync(Guid folderId, string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        var start = await Owner.Client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(Slug, "general", name, bytes.Length, "text/plain", folderId));
        start.EnsureSuccessStatusCode();
        var body = (await start.Content.ReadFromJsonAsync<StartUploadResponse>())!;
        using (var part = new ByteArrayContent(bytes))
        {
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            (await Owner.Client.PutAsync($"/api/v1/uploads/{body.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        }

        var complete = await Owner.Client.PostAsJsonAsync($"/api/v1/uploads/{body.UploadId}/complete", new CompleteUploadRequest(null, null));
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<FileItemDto>())!;
    }
}
