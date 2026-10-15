using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Files;

public enum ListingSortKey
{
    Name,
    Size,
    Created,
    Updated,
    Type,
}

public sealed record ListingSort(ListingSortKey Key, bool Descending)
{
    public static readonly ListingSort Default = new(ListingSortKey.Updated, Descending: true);

    public string Token => $"{Key.ToString().ToLowerInvariant()}_{(Descending ? "desc" : "asc")}";

    /// <summary>Accepts <c>name</c>, <c>name_asc</c>, <c>name_desc</c> (and size/created/updated/type); empty = updated_desc.</summary>
    public static bool TryParse(string? raw, out ListingSort sort)
    {
        sort = Default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var parts = raw.Trim().ToLowerInvariant().Split('_');
        if (parts.Length > 2 || !Enum.TryParse<ListingSortKey>(parts[0], ignoreCase: true, out var key) || int.TryParse(parts[0], out _))
        {
            return false;
        }

        var descending = parts.Length == 2
            ? parts[1] switch
            {
                "asc" => (bool?)false,
                "desc" => true,
                _ => null,
            }
            : false;
        if (descending is null)
        {
            return false;
        }

        sort = new ListingSort(key, descending.Value);
        return true;
    }
}

/// <summary>One row of the folders ∪ files listing: just the sort keys and identity (entities are loaded after paging).</summary>
public sealed class ListingRow
{
    /// <summary>0 = folder, 1 = file. Name and type sorts list folders first in both directions.</summary>
    public int Rank { get; init; }
    public Guid Id { get; init; }
    public string NameKey { get; init; } = "";
    public string TypeKey { get; init; } = "";
    public long Size { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ListingPage(IReadOnlyList<ListingRow> Rows, string? NextCursor);

/// <summary>
/// Keyset pagination over folders and files of one container (channel or personal owner) in SQL. Every sort has
/// a deterministic total order ending in Id, and the cursor is bound to sort + folder + trash + filters, so pages
/// never repeat or skip rows under concurrent inserts/deletes (rows inserted before the cursor are simply not seen).
/// </summary>
public static class ItemListing
{
    public static async Task<AppResult<ListingPage>> PageAsync(
        ApplicationDbContext db,
        Guid containerId,
        Guid? folderId,
        ListItemsQuery query,
        CancellationToken ct)
    {
        if (!ListingSort.TryParse(query.Sort, out var sort))
        {
            return AppResult<ListingPage>.Fail(
                ErrorCodes.Validation,
                "sort must be name, size, created, updated or type, optionally with _asc or _desc.");
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var trash = query.TrashOnly;
        var filterHash = FilterHash(query);

        Cursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            cursor = Cursor.Decode(query.Cursor);
            if (cursor is null
                || cursor.S != sort.Token
                || cursor.F != (trash ? null : folderId)
                || cursor.T != trash
                || cursor.H != filterHash)
            {
                return AppResult<ListingPage>.Fail(ErrorCodes.Validation, "Invalid cursor.");
            }
        }

        var rows = Union(db, containerId, folderId, query);
        if (cursor is not null)
        {
            rows = After(rows, sort, cursor);
        }

        var page = await Order(rows, sort).Take(limit + 1).ToListAsync(ct);
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            next = Cursor.From(page[^1], sort, trash ? null : folderId, trash, filterHash).Encode();
        }

        return AppResult<ListingPage>.Ok(new ListingPage(page, next));
    }

    private static IQueryable<ListingRow> Union(ApplicationDbContext db, Guid containerId, Guid? folderId, ListItemsQuery query)
    {
        var trash = query.TrashOnly;
        var folders = db.Folders.AsNoTracking().Where(f => f.ContainerId == containerId);
        var files = db.Files.AsNoTracking().Where(f => f.ContainerId == containerId);
        if (trash)
        {
            folders = folders.Where(f => f.DeletedAt != null);
            files = files.Where(f => f.DeletedAt != null);
        }
        else
        {
            folders = folders.Where(f => f.DeletedAt == null);
            files = files.Where(f => f.DeletedAt == null);
            // Separate null/non-null branches keep `= @p` / `IS NULL` index-friendly (no OR on parameters).
            if (folderId is Guid parent)
            {
                folders = folders.Where(f => f.ParentId == parent);
                files = files.Where(f => f.FolderId == parent);
            }
            else
            {
                folders = folders.Where(f => f.ParentId == null);
                files = files.Where(f => f.FolderId == null);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var pattern = LikePattern.Contains(query.NameContains.Trim());
            folders = folders.Where(f => EF.Functions.ILike(f.Name, pattern, LikePattern.EscapeCharacter));
            files = files.Where(f => EF.Functions.ILike(f.Name, pattern, LikePattern.EscapeCharacter));
        }

        if (query.UploaderId is Guid uploader)
        {
            files = files.Where(f => f.UploadedBy == uploader);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var typePrefix = LikePattern.StartsWith(query.Type.Trim().ToLowerInvariant());
            files = files.Where(f => EF.Functions.Like(f.ContentType.ToLower(), typePrefix, LikePattern.EscapeCharacter));
        }

        return folders
            .Select(f => new ListingRow
            {
                Rank = 0,
                Id = f.Id,
                NameKey = f.Name.ToLower(),
                TypeKey = "",
                Size = 0L,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
            })
            .Concat(files.Select(f => new ListingRow
            {
                Rank = 1,
                Id = f.Id,
                NameKey = f.Name.ToLower(),
                TypeKey = f.ContentType.ToLower(),
                Size = f.SizeBytes,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
            }));
    }

    private static IQueryable<ListingRow> Order(IQueryable<ListingRow> rows, ListingSort sort) => (sort.Key, sort.Descending) switch
    {
        (ListingSortKey.Name, false) => rows.OrderBy(r => r.Rank).ThenBy(r => r.NameKey).ThenBy(r => r.Id),
        (ListingSortKey.Name, true) => rows.OrderBy(r => r.Rank).ThenByDescending(r => r.NameKey).ThenByDescending(r => r.Id),
        (ListingSortKey.Type, false) => rows.OrderBy(r => r.Rank).ThenBy(r => r.TypeKey).ThenBy(r => r.NameKey).ThenBy(r => r.Id),
        (ListingSortKey.Type, true) => rows.OrderBy(r => r.Rank).ThenByDescending(r => r.TypeKey).ThenByDescending(r => r.NameKey).ThenByDescending(r => r.Id),
        (ListingSortKey.Size, false) => rows.OrderBy(r => r.Size).ThenBy(r => r.Id),
        (ListingSortKey.Size, true) => rows.OrderByDescending(r => r.Size).ThenByDescending(r => r.Id),
        (ListingSortKey.Created, false) => rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
        (ListingSortKey.Created, true) => rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        (ListingSortKey.Updated, false) => rows.OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id),
        _ => rows.OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id),
    };

    /// <summary>Rows strictly after the cursor in the sort's total order.</summary>
    private static IQueryable<ListingRow> After(IQueryable<ListingRow> rows, ListingSort sort, Cursor c)
    {
        var id = c.I;
        var rank = c.R;
        var text = c.A ?? "";
        var text2 = c.B ?? "";
        var number = c.N;
        var time = new DateTimeOffset(c.N, TimeSpan.Zero);
        return (sort.Key, sort.Descending) switch
        {
            (ListingSortKey.Name, false) => rows.Where(r => r.Rank > rank
                || (r.Rank == rank && (string.Compare(r.NameKey, text) > 0 || (r.NameKey == text && r.Id.CompareTo(id) > 0)))),
            (ListingSortKey.Name, true) => rows.Where(r => r.Rank > rank
                || (r.Rank == rank && (string.Compare(r.NameKey, text) < 0 || (r.NameKey == text && r.Id.CompareTo(id) < 0)))),
            (ListingSortKey.Type, false) => rows.Where(r => r.Rank > rank
                || (r.Rank == rank && (string.Compare(r.TypeKey, text) > 0
                    || (r.TypeKey == text && (string.Compare(r.NameKey, text2) > 0 || (r.NameKey == text2 && r.Id.CompareTo(id) > 0)))))),
            (ListingSortKey.Type, true) => rows.Where(r => r.Rank > rank
                || (r.Rank == rank && (string.Compare(r.TypeKey, text) < 0
                    || (r.TypeKey == text && (string.Compare(r.NameKey, text2) < 0 || (r.NameKey == text2 && r.Id.CompareTo(id) < 0)))))),
            (ListingSortKey.Size, false) => rows.Where(r => r.Size > number || (r.Size == number && r.Id.CompareTo(id) > 0)),
            (ListingSortKey.Size, true) => rows.Where(r => r.Size < number || (r.Size == number && r.Id.CompareTo(id) < 0)),
            (ListingSortKey.Created, false) => rows.Where(r => r.CreatedAt > time || (r.CreatedAt == time && r.Id.CompareTo(id) > 0)),
            (ListingSortKey.Created, true) => rows.Where(r => r.CreatedAt < time || (r.CreatedAt == time && r.Id.CompareTo(id) < 0)),
            (ListingSortKey.Updated, false) => rows.Where(r => r.UpdatedAt > time || (r.UpdatedAt == time && r.Id.CompareTo(id) > 0)),
            _ => rows.Where(r => r.UpdatedAt < time || (r.UpdatedAt == time && r.Id.CompareTo(id) < 0)),
        };
    }

    private static string FilterHash(ListItemsQuery query)
    {
        var raw = $"{query.NameContains?.Trim()}\u001f{query.Type?.Trim().ToLowerInvariant()}\u001f{query.UploaderId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)).AsSpan(0, 8));
    }

    /// <summary>Opaque position: sort token, folder, trash flag and filter hash it belongs to, plus the last row's keys.</summary>
    private sealed record Cursor(string S, Guid? F, bool T, string H, int R, string? A, string? B, long N, Guid I)
    {
        public static Cursor From(ListingRow row, ListingSort sort, Guid? folderId, bool trash, string filterHash) => sort.Key switch
        {
            ListingSortKey.Name => new(sort.Token, folderId, trash, filterHash, row.Rank, row.NameKey, null, 0, row.Id),
            ListingSortKey.Type => new(sort.Token, folderId, trash, filterHash, row.Rank, row.TypeKey, row.NameKey, 0, row.Id),
            ListingSortKey.Size => new(sort.Token, folderId, trash, filterHash, row.Rank, null, null, row.Size, row.Id),
            ListingSortKey.Created => new(sort.Token, folderId, trash, filterHash, row.Rank, null, null, row.CreatedAt.UtcTicks, row.Id),
            _ => new(sort.Token, folderId, trash, filterHash, row.Rank, null, null, row.UpdatedAt.UtcTicks, row.Id),
        };

        public string Encode() =>
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static Cursor? Decode(string raw)
        {
            if (raw.Length > 2048)
            {
                return null;
            }

            try
            {
                var b64 = raw.Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');
                return JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(b64));
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                return null;
            }
        }
    }
}
