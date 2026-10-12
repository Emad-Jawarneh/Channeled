using System.Globalization;
using System.Text;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Search;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Files;
using Channeled.Domain.Search;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Search;

public sealed class SearchService(
    ApplicationDbContext db,
    IAuthorizer authorizer) : ISearchService
{
    public async Task<AppResult<SearchResponse>> SearchAsync(Guid userId, SearchQuery query, CancellationToken ct = default)
    {
        var q = query.Q?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(q) || q.Length > 200)
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.Validation, "Query is required.");
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        // Strip LIKE metacharacters; trigram GIN accelerates ILIKE '%q%'.
        var safe = new string(q.Where(c => c is not ('%' or '_')).ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.Validation, "Query is required.");
        }

        var pattern = "%" + safe + "%";

        return query.Scope switch
        {
            SearchScope.Personal => await SearchPersonalAsync(userId, q, pattern, query, limit, ct),
            SearchScope.Channel => await SearchChannelAsync(userId, q, pattern, query, limit, ct),
            SearchScope.Team => await SearchTeamAsync(userId, q, pattern, query, limit, ct),
            _ => AppResult<SearchResponse>.Fail(ErrorCodes.Validation, "Unknown search scope."),
        };
    }

    private async Task<AppResult<SearchResponse>> SearchPersonalAsync(
        Guid userId,
        string q,
        string pattern,
        SearchQuery query,
        int limit,
        CancellationToken ct)
    {
        if (!authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = AuthAction.AccessPersonal,
            IsPersonalOwner = true,
        }).Allowed)
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var files = db.Files.AsNoTracking()
            .Where(f => f.OwnerUserId == userId && f.DeletedAt == null)
            .Where(f => EF.Functions.ILike(f.Name, pattern));

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim().ToLowerInvariant();
            files = files.Where(f => f.ContentType.ToLower().StartsWith(type));
        }

        if (query.UploaderId is Guid uploader)
        {
            files = files.Where(f => f.UploadedBy == uploader);
        }

        var rows = await files
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.Id)
            .Take(500)
            .ToListAsync(ct);

        // Prefer closer name matches first via in-memory similarity after trigram-backed ILIKE filter.
        rows = rows
            .OrderByDescending(f => TrigramScore(f.Name, q))
            .ThenByDescending(f => f.UpdatedAt)
            .ToList();

        return PageHits(rows.Select(f => ToHit(f, null, null)), query.Cursor, limit);
    }

    private async Task<AppResult<SearchResponse>> SearchChannelAsync(
        Guid userId,
        string q,
        string pattern,
        SearchQuery query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.TeamSlug) || string.IsNullOrWhiteSpace(query.ChannelName))
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.Validation, "teamSlug and channelName are required for channel scope.");
        }

        var access = await LoadChannelAsync(userId, query.TeamSlug, query.ChannelName, ct);
        if (access is null || !AllowBrowse(userId, access))
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        // Push filter + ranking into Postgres so gin_trgm_ops is used (M5 bench).
        var typePattern = string.IsNullOrWhiteSpace(query.Type)
            ? "%"
            : query.Type.Trim().ToLowerInvariant() + "%";
        var uploader = query.UploaderId ?? Guid.Empty;
        var filterUploader = query.UploaderId.HasValue;

        var rows = await db.Files.FromSqlInterpolated(
                $"""
                 SELECT f.*
                 FROM files AS f
                 WHERE f."ChannelId" = {access.Channel.Id}
                   AND f."DeletedAt" IS NULL
                   AND f."Name" ILIKE {pattern}
                   AND lower(f."ContentType") LIKE {typePattern}
                   AND ({!filterUploader} OR f."UploadedBy" = {uploader})
                 ORDER BY similarity(f."Name", {q}) DESC, f."UpdatedAt" DESC
                 LIMIT {Math.Min(limit + 1, 200)}
                 """)
            .AsNoTracking()
            .ToListAsync(ct);

        return PageHits(
            rows.Select(f => ToHit(f, access.Channel.Name, access.Team.Slug)),
            query.Cursor,
            limit);
    }

    private async Task<AppResult<SearchResponse>> SearchTeamAsync(
        Guid userId,
        string q,
        string pattern,
        SearchQuery query,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.TeamSlug))
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.Validation, "teamSlug is required for team scope.");
        }

        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == query.TeamSlug, ct);
        if (team is null)
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null
            || !authorizer.Authorize(new AuthorizationContext
            {
                UserId = userId,
                Action = AuthAction.Search,
                IsTeamMember = true,
                TeamRole = member.Role,
                TeamArchived = team.ArchivedAt is not null,
            }).Allowed)
        {
            return AppResult<SearchResponse>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        var channels = await db.Channels.AsNoTracking()
            .Where(c => c.TeamId == team.Id)
            .ToListAsync(ct);
        var memberships = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId)
            .ToListAsync(ct);
        var memberSet = memberships.ToHashSet();

        var visibleChannelIds = new List<Guid>();
        foreach (var channel in channels)
        {
            var access = new ChannelAccess(team, member.Role, channel, memberSet.Contains(channel.Id), false, false);
            if (AllowBrowse(userId, access))
            {
                visibleChannelIds.Add(channel.Id);
            }
        }

        var channelNameById = channels.ToDictionary(c => c.Id, c => c.Name);

        var files = await db.Files.AsNoTracking()
            .Where(f => f.ChannelId != null && visibleChannelIds.Contains(f.ChannelId.Value) && f.DeletedAt == null)
            .Where(f => EF.Functions.ILike(f.Name, pattern))
            .Take(500)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim().ToLowerInvariant();
            files = files.Where(f => f.ContentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (query.UploaderId is Guid uploader)
        {
            files = files.Where(f => f.UploadedBy == uploader).ToList();
        }

        var combined = files
            .OrderByDescending(f => TrigramScore(f.Name, q))
            .ThenByDescending(f => f.UpdatedAt)
            .Select(f => ToHit(f, f.ChannelId is Guid cid ? channelNameById.GetValueOrDefault(cid) : null, team.Slug))
            .Concat(
                channels
                    .Where(c => visibleChannelIds.Contains(c.Id)
                                && c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => TrigramScore(c.Name, q))
                    .Select(c => new SearchHitDto(
                        c.Id,
                        ItemKind.Folder,
                        c.Name,
                        c.Id,
                        null,
                        "channel",
                        null,
                        null,
                        c.CreatedAt,
                        c.Name,
                        team.Slug)))
            .ToList();

        return PageHits(combined, query.Cursor, limit);
    }

    private bool AllowBrowse(Guid userId, ChannelAccess access) =>
        authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = AuthAction.BrowseFiles,
            IsTeamMember = true,
            TeamRole = access.Role,
            TeamArchived = access.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = access.Channel.Visibility,
            ChannelArchived = access.Channel.ArchivedAt is not null,
            IsChannelMember = access.IsChannelMember,
            ChannelCanWrite = access.CanWrite,
            ChannelCanManage = access.CanManage,
        }).Allowed;

    private async Task<ChannelAccess?> LoadChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (team is null)
        {
            return null;
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return null;
        }

        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TeamId == team.Id && c.Name.ToLower() == channelName.ToLower(), ct);
        if (channel is null)
        {
            return null;
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);

        return new ChannelAccess(team, member.Role, channel, cm is not null, cm?.CanWrite ?? false, cm?.CanManage ?? false);
    }

    private static AppResult<SearchResponse> PageHits(IEnumerable<SearchHitDto> hits, string? cursor, int limit)
    {
        var ordered = hits.ToList();
        if (!string.IsNullOrWhiteSpace(cursor) && TryDecodeCursor(cursor, out var t, out var id))
        {
            ordered = ordered
                .Where(h => h.UpdatedAt < t || (h.UpdatedAt == t && h.Id.CompareTo(id) < 0))
                .ToList();
        }

        var page = ordered.Take(limit + 1).ToList();
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            next = EncodeCursor(page[^1].UpdatedAt, page[^1].Id);
        }

        return AppResult<SearchResponse>.Ok(new SearchResponse(page, next));
    }

    private static SearchHitDto ToHit(FileEntity f, string? channelName, string? teamSlug) =>
        new(f.Id, ItemKind.File, f.Name, f.ChannelId, f.OwnerUserId, f.ContentType, f.SizeBytes, f.UploadedBy, f.UpdatedAt, channelName, teamSlug);

    /// <summary>Lightweight trigram-ish score for ranking filtered rows.</summary>
    public static double TrigramScore(string name, string query)
    {
        var a = name.ToLowerInvariant();
        var b = query.ToLowerInvariant();
        if (a.Contains(b, StringComparison.Ordinal))
        {
            return 1.0 + (b.Length / (double)Math.Max(a.Length, 1));
        }

        var gramsA = Grams(a);
        var gramsB = Grams(b);
        if (gramsA.Count == 0 || gramsB.Count == 0)
        {
            return 0;
        }

        var inter = gramsA.Intersect(gramsB).Count();
        return 2.0 * inter / (gramsA.Count + gramsB.Count);
    }

    private static HashSet<string> Grams(string s)
    {
        var padded = "  " + s + " ";
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < padded.Length - 2; i++)
        {
            set.Add(padded.Substring(i, 3));
        }

        return set;
    }

    private static string EncodeCursor(DateTimeOffset updatedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{updatedAt.UtcTicks}:{id:N}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset updatedAt, out Guid id)
    {
        updatedAt = default;
        id = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            updatedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);
}
