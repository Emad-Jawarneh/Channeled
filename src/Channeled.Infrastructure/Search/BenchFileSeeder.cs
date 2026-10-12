using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Search;

public sealed class BenchFileSeeder(ApplicationDbContext db)
{
    public async Task<(Guid TeamId, string TeamSlug, Guid ChannelId, Guid UserId, int Inserted)> SeedAsync(
        Guid userId,
        int count,
        CancellationToken ct = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));

        var slug = "bench-" + UuidV7.NewGuid().ToString("N")[..8];
        var team = new TeamEntity
        {
            Id = UuidV7.NewGuid(),
            Name = "Bench Team",
            Slug = slug,
            QuotaBytes = long.MaxValue / 4,
            UsageBytes = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var channel = new ChannelEntity
        {
            Id = UuidV7.NewGuid(),
            TeamId = team.Id,
            Name = "general",
            Visibility = ChannelVisibility.Open,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMemberEntity
        {
            TeamId = team.Id,
            UserId = userId,
            Role = MemberRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);

        const int batchSize = 100_000;
        var inserted = 0;
        for (var start = 1; start <= count; start += batchSize)
        {
            var end = Math.Min(start + batchSize - 1, count);
            var batch = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO files ("Id", "FolderId", "ChannelId", "OwnerUserId", "Name", "ContentType", "SizeBytes", "Sha256", "StorageKey", "UploadedBy", "CreatedAt", "UpdatedAt", "DeletedAt")
                 SELECT gen_random_uuid(),
                        NULL,
                        {channel.Id},
                        NULL,
                        'bench-file-' || g || '.dat',
                        'application/octet-stream',
                        100,
                        repeat('0', 64),
                        'objects/bench/' || g,
                        {userId},
                        NOW(),
                        NOW(),
                        NULL
                 FROM generate_series({start}, {end}) AS g
                 """,
                ct);
            inserted += batch;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE files
             SET "Name" = 'needle-channeled-report.pdf'
             WHERE "ChannelId" = {channel.Id}
               AND "Name" = 'bench-file-' || {count} || '.dat'
             """,
            ct);

        team.UsageBytes = 100L * count;
        await db.SaveChangesAsync(ct);

        return (team.Id, slug, channel.Id, userId, inserted);
    }
}
