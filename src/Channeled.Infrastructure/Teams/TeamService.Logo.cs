using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Storage;

namespace Channeled.Infrastructure.Teams;

public sealed partial class TeamService
{
    private const long MaxLogoBytes = 512 * 1024;
    private static readonly HashSet<string> AllowedLogoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
    };

    public async Task<AppResult<TeamDto>> SetTeamLogoAsync(
        Guid userId,
        string slug,
        Stream content,
        string contentType,
        long? contentLength,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam<TeamDto>();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.EditTeamSettings, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam<TeamDto>();
        }

        var normalizedType = NormalizeLogoContentType(contentType);
        if (normalizedType is null)
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Logo must be a PNG, JPEG, WebP, or GIF image.");
        }

        if (contentLength is > MaxLogoBytes)
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Logo must be 512 KB or smaller.");
        }

        await using var buffer = new MemoryStream(capacity: (int)Math.Min(contentLength ?? 64 * 1024, MaxLogoBytes));
        var copied = await CopyLimitedAsync(content, buffer, MaxLogoBytes + 1, ct);
        if (copied > MaxLogoBytes)
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Logo must be 512 KB or smaller.");
        }

        if (copied == 0)
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Logo file is empty.");
        }

        buffer.Position = 0;
        var key = ObjectKey.ForTeamLogo(loaded.Team.Id);
        await store.PutAsync(key, buffer, normalizedType, ct);

        loaded.Team.LogoObjectKey = key;
        loaded.Team.LogoContentType = normalizedType;
        loaded.Team.LogoUpdatedAt = clock.UtcNow;
        await WriteAuditAsync(
            userId,
            AuditEventType.TeamLogoChange,
            "team",
            loaded.Team.Id.ToString(),
            loaded.Team.Id,
            context,
            ct,
            JsonSerializer.Serialize(new { action = "set", contentType = normalizedType, bytes = copied }));
        await db.SaveChangesAsync(ct);

        return AppResult<TeamDto>.Ok(ToTeamDto(loaded.Team, loaded.Role));
    }

    public async Task<AppResult<TeamDto>> ClearTeamLogoAsync(
        Guid userId,
        string slug,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam<TeamDto>();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.EditTeamSettings, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam<TeamDto>();
        }

        var previousKey = loaded.Team.LogoObjectKey;
        loaded.Team.LogoObjectKey = null;
        loaded.Team.LogoContentType = null;
        loaded.Team.LogoUpdatedAt = null;
        await WriteAuditAsync(
            userId,
            AuditEventType.TeamLogoChange,
            "team",
            loaded.Team.Id.ToString(),
            loaded.Team.Id,
            context,
            ct,
            JsonSerializer.Serialize(new { action = "cleared" }));
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previousKey))
        {
            try
            {
                await store.DeleteAsync(previousKey, ct);
            }
            catch
            {
                // Best-effort cleanup; metadata already cleared.
            }
        }

        return AppResult<TeamDto>.Ok(ToTeamDto(loaded.Team, loaded.Role));
    }

    public async Task<AppResult<TeamLogoResult>> OpenTeamLogoAsync(
        Guid userId,
        string slug,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam<TeamLogoResult>();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.ViewTeam, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam<TeamLogoResult>();
        }

        if (string.IsNullOrEmpty(loaded.Team.LogoObjectKey) || string.IsNullOrEmpty(loaded.Team.LogoContentType))
        {
            return AppResult<TeamLogoResult>.Fail(ErrorCodes.NotFound, "Team logo not found.");
        }

        var stream = await store.OpenReadAsync(loaded.Team.LogoObjectKey, cancellationToken: ct);
        return AppResult<TeamLogoResult>.Ok(new TeamLogoResult(stream, loaded.Team.LogoContentType));
    }

    private static string? NormalizeLogoContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var type = contentType.Split(';', 2)[0].Trim();
        return AllowedLogoTypes.Contains(type) ? type.ToLowerInvariant() : null;
    }

    private static async Task<long> CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return total;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }
}
