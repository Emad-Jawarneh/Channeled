using Channeled.Application.Files;
using Channeled.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Files;

/// <summary>Ancestor walk for one item in a single recursive query: folder path plus trashed-ancestor detection.</summary>
public static class FolderChain
{
    private const int MaxDepth = 64;

    public sealed class Row
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset? DeletedAt { get; set; }
        public int Depth { get; set; }
    }

    /// <summary>Folders from <paramref name="parentFolderId"/> up to the root, nearest first.</summary>
    public static async Task<IReadOnlyList<Row>> LoadAsync(ApplicationDbContext db, Guid? parentFolderId, CancellationToken ct)
    {
        if (parentFolderId is not Guid start)
        {
            return [];
        }

        return await db.Database.SqlQuery<Row>(
                $"""
                 WITH RECURSIVE chain AS (
                     SELECT f."Id", f."ParentId", f."Name", f."DeletedAt", 0 AS "Depth"
                     FROM folders AS f
                     WHERE f."Id" = {start}
                     UNION ALL
                     SELECT p."Id", p."ParentId", p."Name", p."DeletedAt", c."Depth" + 1
                     FROM folders AS p
                     JOIN chain AS c ON p."Id" = c."ParentId"
                     WHERE c."Depth" < {MaxDepth}
                 )
                 SELECT "Id", "Name", "DeletedAt", "Depth" FROM chain
                 """)
            .OrderBy(r => r.Depth)
            .ToListAsync(ct);
    }

    public static ItemLocationDto Locate(
        string scope,
        string? teamSlug,
        string? channelName,
        FileItemDto item,
        Guid? parentFolderId,
        DateTimeOffset? itemDeletedAt,
        IReadOnlyList<Row> chain)
    {
        // Outermost trashed node wins: under folder trash, inner rows are hidden behind it in the trash list.
        Guid? trashEntry = itemDeletedAt is null ? null : item.Id;
        foreach (var folder in chain)
        {
            if (folder.DeletedAt is not null)
            {
                trashEntry = folder.Id;
            }
        }

        var path = chain.Reverse().Select(f => new FolderPathSegmentDto(f.Id, f.Name)).ToList();
        return new ItemLocationDto(
            trashEntry is null ? "live" : "trashed",
            scope,
            teamSlug,
            channelName,
            parentFolderId,
            path,
            item,
            trashEntry);
    }
}
