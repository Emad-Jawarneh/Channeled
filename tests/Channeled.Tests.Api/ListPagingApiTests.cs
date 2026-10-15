using System.Net;
using System.Net.Http.Json;
using Channeled.Application.Files;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Tests.Api;

/// <summary>SQL keyset listing: every sort walks to the full set with no repeats or skips, in both scopes and in trash.</summary>
[Collection("Api")]
public class ListPagingApiTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{
    private Guid _channelId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _channelId = (await CreateChannelAsync("general", ChannelVisibility.Everyone)).Id;
    }

    private sealed record Seeded(Guid Id, bool IsFolder, string Name, long Size, DateTimeOffset Created, DateTimeOffset Updated, string ContentType);

    /// <summary>
    /// 3 folders + 37 files with deliberately repeated sizes and timestamps (so Id tiebreaks matter) and mixed-case
    /// alphanumeric names (glibc collation equals ordinal for [a-z0-9] after lower()).
    /// </summary>
    private async Task<List<Seeded>> SeedAsync(bool personal, bool trashed, Guid? folderId = null)
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var types = new[] { "text/plain", "image/png", "application/pdf" };
        var seeded = new List<Seeded>();
        await DbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var id = UuidV7.NewGuid();
                var t = baseTime.AddMinutes(i % 2);
                db.Folders.Add(new FolderEntity
                {
                    Id = id,
                    ChannelId = personal ? null : _channelId,
                    OwnerUserId = personal ? Owner.Id : null,
                    ParentId = folderId,
                    Name = $"Folder{i:D2}",
                    CreatedAt = t,
                    UpdatedAt = t,
                    DeletedAt = trashed ? baseTime.AddDays(1) : null,
                });
                seeded.Add(new Seeded(id, true, $"folder{i:D2}", 0, t, t, ""));
            }

            for (var i = 0; i < 37; i++)
            {
                var id = UuidV7.NewGuid();
                var name = (i % 3) switch { 0 => $"Item{i:D3}", 1 => $"item{i:D3}", _ => $"ITEM{i:D3}" };
                var created = baseTime.AddMinutes(i % 5);
                var updated = baseTime.AddMinutes(i % 4);
                var size = (i % 6) * 100L;
                var type = types[i % types.Length];
                db.Files.Add(new FileEntity
                {
                    Id = id,
                    ChannelId = personal ? null : _channelId,
                    OwnerUserId = personal ? Owner.Id : null,
                    FolderId = folderId,
                    Name = name,
                    ContentType = type,
                    SizeBytes = size,
                    Sha256 = new string('a', 64),
                    StorageKey = "seed/" + id,
                    UploadedBy = Owner.Id,
                    CreatedAt = created,
                    UpdatedAt = updated,
                    DeletedAt = trashed ? baseTime.AddDays(1) : null,
                });
                seeded.Add(new Seeded(id, false, name.ToLowerInvariant(), size, created, updated, type));
            }

            await db.SaveChangesAsync();
        });
        return seeded;
    }

    private static IEnumerable<Seeded> Expected(IEnumerable<Seeded> rows, string sort)
    {
        var rank = (Seeded s) => s.IsFolder ? 0 : 1;
        var id = (Seeded s) => s.Id;
        return sort switch
        {
            "name_asc" => rows.OrderBy(rank).ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(id),
            "name_desc" => rows.OrderBy(rank).ThenByDescending(s => s.Name, StringComparer.Ordinal).ThenByDescending(id),
            "type_asc" => rows.OrderBy(rank).ThenBy(s => s.ContentType, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(id),
            "type_desc" => rows.OrderBy(rank).ThenByDescending(s => s.ContentType, StringComparer.Ordinal).ThenByDescending(s => s.Name, StringComparer.Ordinal).ThenByDescending(id),
            "size_asc" => rows.OrderBy(s => s.Size).ThenBy(id),
            "size_desc" => rows.OrderByDescending(s => s.Size).ThenByDescending(id),
            "created_asc" => rows.OrderBy(s => s.Created).ThenBy(id),
            "created_desc" => rows.OrderByDescending(s => s.Created).ThenByDescending(id),
            "updated_asc" => rows.OrderBy(s => s.Updated).ThenBy(id),
            _ => rows.OrderByDescending(s => s.Updated).ThenByDescending(id),
        };
    }

    private string ListUrl(bool personal, string query) =>
        personal ? $"/api/v1/me/files/items?{query}" : TeamUrl($"/channels/general/items?{query}");

    private async Task<List<FileItemDto>> WalkAsync(bool personal, string baseQuery, int limit = 7)
    {
        var all = new List<FileItemDto>();
        string? cursor = null;
        for (var i = 0; i < 100; i++)
        {
            var q = $"{baseQuery}&limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await Owner.Client.GetAsync(ListUrl(personal, q));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var page = (await response.Content.ReadFromJsonAsync<ItemListResponse>())!;
            page.Items.Count.Should().BeLessThanOrEqualTo(limit);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is null)
            {
                return all;
            }
        }

        throw new InvalidOperationException("Paging did not terminate.");
    }

    public static TheoryData<string, bool, bool> Matrix()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var sort in new[] { "name_asc", "name_desc", "size_asc", "size_desc", "created_asc", "created_desc", "updated_asc", "updated_desc", "type_asc", "type_desc" })
        {
            data.Add(sort, false, false);
            data.Add(sort, true, false);
        }

        data.Add("name_asc", false, true);
        data.Add("updated_desc", true, true);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Walking_all_pages_returns_every_row_once_in_sort_order(string sort, bool personal, bool trash)
    {
        var seeded = await SeedAsync(personal, trash);
        var listed = await WalkAsync(personal, $"sort={sort}" + (trash ? "&trash=true" : ""));

        listed.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        listed.Select(i => i.Id).Should().Equal(Expected(seeded, sort).Select(s => s.Id));
        if (sort.StartsWith("name", StringComparison.Ordinal) || sort.StartsWith("type", StringComparison.Ordinal))
        {
            listed.TakeWhile(i => i.Kind == Channeled.Domain.Files.ItemKind.Folder).Should().HaveCount(3, "folders come first in both directions");
        }
    }

    [Fact]
    public async Task Default_sort_is_updated_desc_and_folder_scoped()
    {
        var parent = UuidV7.NewGuid();
        await DbAsync(async db =>
        {
            db.Folders.Add(new FolderEntity { Id = parent, ChannelId = _channelId, Name = "Parent", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var inside = await SeedAsync(personal: false, trashed: false, parent);
        var listed = await WalkAsync(false, $"folderId={parent}");
        listed.Select(i => i.Id).Should().Equal(Expected(inside, "updated_desc").Select(s => s.Id));
    }

    [Fact]
    public async Task Pages_stay_consistent_when_rows_are_inserted_and_deleted_between_requests()
    {
        var seeded = await SeedAsync(personal: false, trashed: false);
        var ordered = Expected(seeded, "name_asc").ToList();
        var first = (await Owner.Client.GetFromJsonAsync<ItemListResponse>(TeamUrl("/channels/general/items?sort=name_asc&limit=10")))!;
        first.Items.Select(i => i.Id).Should().Equal(ordered.Take(10).Select(s => s.Id));

        // Insert one row that sorts before the cursor and one after; delete a row that is still ahead.
        var before = UuidV7.NewGuid();
        var after = UuidV7.NewGuid();
        var doomed = ordered[15].Id;
        await DbAsync(async db =>
        {
            foreach (var (id, name) in new[] { (before, "aaa000"), (after, "zzz999") })
            {
                db.Files.Add(new FileEntity
                {
                    Id = id, ChannelId = _channelId, Name = name, ContentType = "text/plain", SizeBytes = 1,
                    Sha256 = new string('b', 64), StorageKey = "seed/" + id, UploadedBy = Owner.Id,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.Files.Where(f => f.Id == doomed).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
        });

        var rest = new List<FileItemDto>();
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = (await Owner.Client.GetFromJsonAsync<ItemListResponse>(
                TeamUrl($"/channels/general/items?sort=name_asc&limit=10&cursor={Uri.EscapeDataString(cursor)}")))!;
            rest.AddRange(page.Items);
            cursor = page.NextCursor;
        }

        var all = first.Items.Concat(rest).Select(i => i.Id).ToList();
        all.Should().OnlyHaveUniqueItems();
        all.Should().Contain(after).And.NotContain(before).And.NotContain(doomed);
        all.Should().HaveCount(seeded.Count, "one insert landed behind the cursor and one row was deleted");
    }

    [Fact]
    public async Task Cursors_are_bound_to_sort_folder_trash_and_filters()
    {
        await SeedAsync(personal: false, trashed: false);
        var page = (await Owner.Client.GetFromJsonAsync<ItemListResponse>(TeamUrl("/channels/general/items?sort=name_asc&limit=5")))!;
        var cursor = Uri.EscapeDataString(page.NextCursor!);

        foreach (var query in new[]
                 {
                     $"sort=size_asc&cursor={cursor}",
                     $"sort=name_asc&trash=true&cursor={cursor}",
                     $"sort=name_asc&q=item&cursor={cursor}",
                     $"sort=name_asc&folderId={Guid.NewGuid()}&cursor={cursor}",
                     "sort=name_asc&cursor=not-a-cursor",
                     "sort=bogus",
                 })
        {
            var response = await Owner.Client.GetAsync(TeamUrl($"/channels/general/items?{query}"));
            response.StatusCode.Should().BeOneOf([HttpStatusCode.BadRequest, HttpStatusCode.NotFound], query);
        }
    }

    [Theory]
    [InlineData("my_file", "my_file.txt")]
    [InlineData("100%", "100%.txt")]
    [InlineData("a\\b", "a\\b.txt")]
    [InlineData("MY_FILE", "my_file.txt")]
    public async Task Name_filter_matches_wildcard_characters_literally(string q, string expected)
    {
        await DbAsync(async db =>
        {
            foreach (var name in new[] { "my_file.txt", "myXfile.txt", "100%.txt", "100X.txt", "a\\b.txt", "ab.txt" })
            {
                var id = UuidV7.NewGuid();
                db.Files.Add(new FileEntity
                {
                    Id = id, ChannelId = _channelId, Name = name, ContentType = "text/plain", SizeBytes = 1,
                    Sha256 = new string('c', 64), StorageKey = "seed/" + id, UploadedBy = Owner.Id,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        });

        var listed = await WalkAsync(false, $"q={Uri.EscapeDataString(q)}");
        listed.Select(i => i.Name).Should().Equal(expected);
    }

    [Fact]
    public async Task Another_users_personal_folder_is_not_found()
    {
        var stranger = await NewUserAsync("Sven");
        var folder = await (await Owner.Client.PostAsJsonAsync("/api/v1/me/files/folders", new CreateFolderRequest("Private", null)))
            .Content.ReadFromJsonAsync<FileItemDto>();
        (await stranger.Client.GetAsync($"/api/v1/me/files/items?folderId={folder!.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
