using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Channeled.Application.Chat;
using Channeled.Application.Files;
using Channeled.Application.Personal;
using Channeled.Domain.Audit;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Chat;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class ChatApiTests : IClassFixture<ChatWorldFixture>
{
    private readonly ChatWorld _w;

    public ChatApiTests(ChatWorldFixture fixture) => _w = fixture.World;

    // ---------- Authorization matrix over HTTP ----------

    [Fact]
    public async Task Open_text_channel_http_matrix()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);

        // Post: Owner/Admin/Member yes, Viewer 403 (visible but read-only), outsider 404 (hidden).
        var ownerMsg = await _w.PostOkAsync(_w.Owner, ch, "from owner");
        var adminMsg = await _w.PostOkAsync(_w.Admin, ch, "from admin");
        var memberMsg = await _w.PostOkAsync(_w.Member, ch, "from member");

        var viewerPost = await _w.PostAsync(_w.Viewer, ch, "nope");
        viewerPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ChatWorld.ErrorCodeAsync(viewerPost)).Should().Be("forbidden");
        (await _w.PostAsync(_w.Outsider, ch, "nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Read: every team member; outsider 404.
        foreach (var reader in new[] { _w.Owner, _w.Admin, _w.Member, _w.Viewer })
        {
            (await _w.ListOkAsync(reader, ch)).Items.Should().HaveCount(3, because: reader.Email);
        }

        (await _w.ListAsync(_w.Outsider, ch)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Delete: member cannot delete someone else's; viewer cannot; outsider 404.
        (await _w.DeleteAsync(_w.Member, ch, ownerMsg.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _w.DeleteAsync(_w.Viewer, ch, ownerMsg.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _w.DeleteAsync(_w.Outsider, ch, ownerMsg.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Member deletes own; admin moderates anyone's.
        (await _w.DeleteAsync(_w.Member, ch, memberMsg.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _w.DeleteAsync(_w.Admin, ch, ownerMsg.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _w.DeleteAsync(_w.Owner, ch, adminMsg.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Already deleted / unknown -> 404.
        (await _w.DeleteAsync(_w.Owner, ch, ownerMsg.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _w.DeleteAsync(_w.Owner, ch, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _w.ListOkAsync(_w.Owner, ch)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Private_text_channel_http_matrix_including_can_write()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text, ChannelVisibility.Private, "vip");

        // Not listed: hidden entirely (404), even for a regular Member.
        (await _w.ListAsync(_w.Member, ch)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _w.PostAsync(_w.Member, ch, "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _w.ListAsync(_w.Viewer, ch)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _w.ListAsync(_w.Outsider, ch)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Listed with can_write=false: can read, cannot post.
        await _w.SetChannelMemberAsync(ch, _w.Member, canWrite: false);
        (await _w.ListAsync(_w.Member, ch)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _w.PostAsync(_w.Member, ch, "x")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Listed with can_write=true: can post and delete own.
        await _w.SetChannelMemberAsync(ch, _w.Member, canWrite: true);
        var mine = await _w.PostOkAsync(_w.Member, ch, "listed writer");
        (await _w.DeleteAsync(_w.Member, ch, mine.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Admin is never gated by the channel list.
        await _w.PostOkAsync(_w.Admin, ch, "admin without listing");

        // Viewer is never a writer, even listed with can_write=true.
        await _w.SetChannelMemberAsync(ch, _w.Viewer, canWrite: true);
        (await _w.ListAsync(_w.Viewer, ch)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _w.PostAsync(_w.Viewer, ch, "x")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A member whose can_write is revoked can no longer delete their own message (write access required).
        var second = await _w.PostOkAsync(_w.Member, ch, "second");
        await _w.SetChannelMemberAsync(ch, _w.Member, canWrite: false);
        (await _w.DeleteAsync(_w.Member, ch, second.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _w.DeleteAsync(_w.Admin, ch, second.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archived_channel_rejects_posts_but_still_lists_and_allows_moderation()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var msg = await _w.PostOkAsync(_w.Member, ch, "before archive");

        (await _w.Owner.Client.PostAsync($"/api/v1/teams/{_w.Slug}/channels/{ch.Name}/archive", null))
            .EnsureSuccessStatusCode();

        var post = await _w.PostAsync(_w.Member, ch, "after archive");
        post.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ChatWorld.ErrorCodeAsync(post)).Should().Be("channel_archived");
        (await _w.ListOkAsync(_w.Member, ch)).Items.Should().ContainSingle();
        (await _w.DeleteAsync(_w.Admin, ch, msg.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------- Channel kind rules ----------

    [Fact]
    public async Task Messages_are_rejected_on_file_channels()
    {
        var files = await _w.CreateChannelAsync(ChannelKind.File);

        var post = await _w.PostAsync(_w.Owner, files, "hello");
        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ChatWorld.ErrorCodeAsync(post)).Should().Be("wrong_channel_kind");
        (await _w.ListAsync(_w.Owner, files)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _w.Owner.Client.GetAsync($"/api/v1/teams/{_w.Slug}/channels/{files.Name}/presence"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Uploads_folders_and_listing_are_rejected_on_text_channels()
    {
        var text = await _w.CreateChannelAsync(ChannelKind.Text);

        var start = await _w.Owner.Client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(
            _w.Slug, text.Name, "a.txt", 1, "text/plain", null));
        start.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var folder = await _w.Owner.Client.PostAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{text.Name}/folders",
            new CreateFolderRequest("Docs", null));
        folder.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _w.Owner.Client.GetAsync($"/api/v1/teams/{_w.Slug}/channels/{text.Name}/items"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Personal copy-to-channel must not land bytes in a text channel either.
        var fileId = await UploadPersonalAsync(_w.Owner, "mine.txt", "personal bytes");
        var copy = await _w.Owner.Client.PostAsJsonAsync(
            $"/api/v1/me/files/items/{fileId}/copy-to-channel",
            new CopyToChannelRequest(_w.Slug, text.Name, null, null));
        copy.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Channel_kind_is_immutable_via_api_and_database()
    {
        var files = await _w.CreateChannelAsync(ChannelKind.File);

        // API: the update contract has no kind; a smuggled field is ignored.
        var update = await _w.Owner.Client.PutAsJsonAsync(
            $"/api/v1/teams/{_w.Slug}/channels/{files.Name}",
            new { name = (string?)null, description = "changed", kind = (int)ChannelKind.Text });
        update.EnsureSuccessStatusCode();
        var channels = await _w.Owner.Client.GetFromJsonAsync<Channeled.Application.Teams.ChannelDto[]>(
            $"/api/v1/teams/{_w.Slug}/channels");
        channels!.Single(c => c.Id == files.Id).Kind.Should().Be(ChannelKind.File);

        // Database: a direct UPDATE of Kind is rejected by trigger.
        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var flip = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE channels SET "Kind" = {(int)ChannelKind.Text} WHERE "Id" = {files.Id}""");
        await flip.Should().ThrowAsync<Exception>();

        // Updates that do not change Kind still work.
        var ok = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE channels SET "Kind" = {(int)ChannelKind.File} WHERE "Id" = {files.Id}""");
        ok.Should().Be(1);
    }

    // ---------- Body limits ----------

    [Fact]
    public async Task Body_limit_4000_ok_4001_rejected_over_http()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);

        (await _w.PostAsync(_w.Member, ch, new string('a', 4000))).StatusCode.Should().Be(HttpStatusCode.Created);

        var tooLong = await _w.PostAsync(_w.Member, ch, new string('a', 4001));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ChatWorld.ErrorCodeAsync(tooLong)).Should().Be("message_too_long");

        // The limit counts characters (code points), exactly like the DB check: 4000 emoji = 8000 UTF-16 units.
        var emoji = char.ConvertFromUtf32(0x1F600);
        (await _w.PostAsync(_w.Member, ch, string.Concat(Enumerable.Repeat(emoji, 4000)))).StatusCode
            .Should().Be(HttpStatusCode.Created);
        (await _w.PostAsync(_w.Member, ch, string.Concat(Enumerable.Repeat(emoji, 4001)))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        foreach (var invalid in new[] { "", "   ", "\n\t", "nul\0byte" })
        {
            (await _w.PostAsync(_w.Member, ch, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest, because: invalid);
        }
    }

    [Fact]
    public async Task Database_check_constraints_enforce_body_length_and_scrub()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        async Task InsertLiveAsync(string body)
        {
            var id = UuidV7.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO channel_messages ("Id", "ChannelId", "AuthorId", "Body", "CreatedAt")
                 VALUES ({id}, {ch.Id}, {_w.Member.Id}, {body}, {now})
                 """);
        }

        async Task InsertDeletedAsync(string body)
        {
            var id = UuidV7.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO channel_messages ("Id", "ChannelId", "AuthorId", "Body", "CreatedAt", "DeletedAt")
                 VALUES ({id}, {ch.Id}, {_w.Member.Id}, {body}, {now}, {now})
                 """);
        }

        await InsertLiveAsync(new string('b', 4000)); // exactly at the limit: accepted
        var over = async () => await InsertLiveAsync(new string('b', 4001));
        await over.Should().ThrowAsync<Exception>();

        var deletedWithBody = async () => await InsertDeletedAsync("still here");
        await deletedWithBody.Should().ThrowAsync<Exception>();
        await InsertDeletedAsync(""); // a scrubbed tombstone is fine
    }

    // ---------- IDs & cursor ----------

    [Fact]
    public async Task Message_ids_are_uuid_v7_and_time_ordered()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var first = await _w.PostOkAsync(_w.Member, ch, "one");
        await Task.Delay(5);
        var second = await _w.PostOkAsync(_w.Member, ch, "two");

        first.Id.ToString()[14].Should().Be('7');
        second.Id.ToString()[14].Should().Be('7');
        // The leading 48 bits are the unix-ms timestamp, so the hex prefix sorts chronologically.
        string.CompareOrdinal(second.Id.ToString("N")[..12], first.Id.ToString("N")[..12]).Should().BeGreaterThan(0);
        second.CreatedAt.Should().BeAfter(first.CreatedAt);
    }

    [Fact]
    public async Task Cursor_is_stable_with_identical_created_at()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var tie = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        await using (var scope = _w.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // 2 older, 7 sharing EXACTLY the same created_at, 1 newer.
            db.ChannelMessages.Add(Seed(ch.Id, tie.AddMinutes(-10), "older-1"));
            db.ChannelMessages.Add(Seed(ch.Id, tie.AddMinutes(-5), "older-2"));
            for (var i = 0; i < 7; i++)
            {
                db.ChannelMessages.Add(Seed(ch.Id, tie, $"tie-{i}"));
            }

            db.ChannelMessages.Add(Seed(ch.Id, tie.AddMinutes(5), "newer"));
            await db.SaveChangesAsync();
        }

        var expected = await ExpectedOrderAsync(ch.Id);
        expected.Should().HaveCount(10);

        // Walk with limit 3 so page boundaries fall inside the tie group.
        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await _w.ListOkAsync(_w.Member, ch, $"limit=3{(cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))}");
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        seen.Should().Equal(expected, because: "no duplicates, no gaps, deterministic (created_at, id) order");
        seen.Should().OnlyHaveUniqueItems();
        pages.Should().Be(4);
    }

    [Fact]
    public async Task Cursor_survives_deleting_the_row_it_points_at()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var tie = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var scope = _w.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < 8; i++)
            {
                db.ChannelMessages.Add(Seed(ch.Id, tie, $"m-{i}"));
            }

            await db.SaveChangesAsync();
        }

        var expected = await ExpectedOrderAsync(ch.Id);
        var page1 = await _w.ListOkAsync(_w.Member, ch, "limit=3");
        page1.Items.Select(i => i.Id).Should().Equal(expected.Take(3));

        // Delete the very row the cursor was minted from, then a NEW post arrives (newer created_at).
        (await _w.DeleteAsync(_w.Admin, ch, page1.Items[^1].Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _w.PostOkAsync(_w.Member, ch, "arrives mid-pagination");

        var page2 = await _w.ListOkAsync(_w.Member, ch, $"limit=10&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        page2.Items.Select(i => i.Id).Should().Equal(expected.Skip(3));
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        (await _w.ListAsync(_w.Member, ch, "cursor=not-a-cursor")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Soft delete, audit, purge ----------

    [Fact]
    public async Task Soft_delete_scrubs_body_immediately_and_audits_only_the_delete()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);

        var auditBeforePosts = await AuditCountAsync();
        var keep = await _w.PostOkAsync(_w.Owner, ch, "keep me");
        var doomed = await _w.PostOkAsync(_w.Member, ch, "top-secret-body-123");
        await _w.PostOkAsync(_w.Member, ch, "another");
        (await AuditCountAsync()).Should().Be(auditBeforePosts, because: "posting is not audited");

        (await _w.DeleteAsync(_w.Member, ch, doomed.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.ChannelMessages.AsNoTracking().SingleAsync(m => m.Id == doomed.Id);
        row.Body.Should().BeEmpty("the body is scrubbed in the same write as the soft delete");
        row.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().Be(_w.Member.Id);

        var list = await _w.ListOkAsync(_w.Owner, ch);
        list.Items.Select(i => i.Id).Should().NotContain(doomed.Id).And.Contain(keep.Id);
        var rawList = await (await _w.ListAsync(_w.Owner, ch)).Content.ReadAsStringAsync();
        rawList.Should().NotContain("top-secret-body-123");

        // Exactly one audit row, for the delete; it never carries the body.
        (await AuditCountAsync()).Should().Be(auditBeforePosts + 1);
        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(a => a.EventType == AuditEventType.ChannelMessageDelete && a.TargetId == doomed.Id.ToString());
        audit.ActorId.Should().Be(_w.Member.Id);
        audit.TeamId.Should().Be(_w.TeamId);
        audit.TargetType.Should().Be("channel_message");
        audit.DetailsJson.Should().NotContain("top-secret-body-123");
        IsModerated(audit.DetailsJson).Should().BeFalse();

        // A moderator deleting someone else's message is flagged as moderated.
        var other = await _w.PostOkAsync(_w.Member, ch, "moderate me");
        (await _w.DeleteAsync(_w.Admin, ch, other.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var moderated = await db.AuditEvents.AsNoTracking()
            .SingleAsync(a => a.EventType == AuditEventType.ChannelMessageDelete && a.TargetId == other.Id.ToString());
        moderated.ActorId.Should().Be(_w.Admin.Id);
        IsModerated(moderated.DetailsJson).Should().BeTrue();
    }

    [Fact]
    public async Task Purge_removes_only_rows_past_retention()
    {
        var ch = await _w.CreateChannelAsync(ChannelKind.Text);
        var now = DateTimeOffset.UtcNow;

        var expiredTombstone = Seed(ch.Id, now.AddDays(-60), "");
        expiredTombstone.DeletedAt = now.AddDays(-45);
        expiredTombstone.DeletedBy = _w.Owner.Id;

        var recentTombstone = Seed(ch.Id, now.AddDays(-60), "");
        recentTombstone.DeletedAt = now.AddDays(-2);
        recentTombstone.DeletedBy = _w.Owner.Id;

        var oldLive = Seed(ch.Id, now.AddDays(-400), "old but alive");

        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ChannelMessages.AddRange(expiredTombstone, recentTombstone, oldLive);
        await db.SaveChangesAsync();

        var auditBefore = await AuditCountAsync();
        var purge = scope.ServiceProvider.GetRequiredService<IChatPurgeService>();
        var removed = await purge.PurgeExpiredAsync();
        removed.Should().BeGreaterThanOrEqualTo(1);

        var remaining = await db.ChannelMessages.AsNoTracking()
            .Where(m => m.ChannelId == ch.Id)
            .Select(m => m.Id)
            .ToListAsync();
        remaining.Should().NotContain(expiredTombstone.Id);
        remaining.Should().Contain(recentTombstone.Id).And.Contain(oldLive.Id);
        (await AuditCountAsync()).Should().Be(auditBefore, because: "purge never rewrites the audit trail");

        (await purge.PurgeExpiredAsync()).Should().Be(0, because: "purge is idempotent");
    }

    // ---------- helpers ----------

    // jsonb re-formats whitespace, so compare parsed values instead of raw text.
    private static bool IsModerated(string detailsJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(detailsJson);
        return doc.RootElement.GetProperty("moderated").GetBoolean();
    }

    private static ChannelMessageEntity Seed(Guid channelId, DateTimeOffset createdAt, string body) => new()
    {
        Id = UuidV7.NewGuid(),
        ChannelId = channelId,
        AuthorId = Guid.NewGuid(),
        Body = body,
        CreatedAt = createdAt,
    };

    private async Task<List<Guid>> ExpectedOrderAsync(Guid channelId)
    {
        // Same ordering the API promises, evaluated by Postgres (uuid byte order) rather than .NET Guid order.
        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ChannelMessages.AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync();
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = _w.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditEvents.CountAsync();
    }

    private static async Task<Guid> UploadPersonalAsync(ChatWorld.Actor actor, string name, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var start = await (await actor.Client.PostAsJsonAsync(
            "/api/v1/me/files/uploads",
            new StartPersonalUploadRequest(name, payload.Length, "text/plain", null)))
            .Content.ReadFromJsonAsync<StartUploadResponse>();
        using var part = new ByteArrayContent(payload);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await actor.Client.PutAsync($"/api/v1/uploads/{start!.UploadId}/parts/1", part)).EnsureSuccessStatusCode();
        var file = await (await actor.Client.PostAsJsonAsync(
            $"/api/v1/uploads/{start.UploadId}/complete",
            new CompleteUploadRequest(sha, null))).Content.ReadFromJsonAsync<FileItemDto>();
        return file!.Id;
    }
}
