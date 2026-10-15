using System.Security.Cryptography;
using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Files;
using Channeled.Application.Personal;
using Channeled.Domain.Errors;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Files;

/// <summary>
/// Short-lived, session-bound download URLs. Tickets are Data Protection time-limited payloads (no extra key
/// configuration), live for the resume window, and carry their issue time so the API can enforce the shorter
/// fresh-start window. ACL and liveness are re-checked on every use.
/// </summary>
public sealed class DownloadTicketService(
    ApplicationDbContext db,
    IFileService files,
    IPersonalService personal,
    IDataProtectionProvider dataProtection,
    IDownloadSlots slots,
    IClock clock,
    IOptionsMonitor<DownloadOptions> options) : IDownloadTicketService
{
    public const string Purpose = "Channeled.DownloadTicket.v1";

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public async Task<AppResult<DownloadTicketsResponse>> IssueAsync(
        Guid userId,
        Guid sessionId,
        DownloadTicketsRequest request,
        CancellationToken ct = default)
    {
        var limits = options.CurrentValue;
        var items = request.Items ?? [];
        if (items.Count == 0 || items.Count > limits.MaxTicketsPerRequest)
        {
            return AppResult<DownloadTicketsResponse>.Fail(
                ErrorCodes.Validation,
                $"Request between 1 and {limits.MaxTicketsPerRequest} downloads at a time.");
        }

        var userFree = Math.Max(0, limits.MaxConcurrentPerUser - slots.ActiveFor(userId));
        var globalFree = Math.Max(0, limits.MaxConcurrentGlobal - slots.ActiveGlobal);
        var capacity = Math.Min(userFree, globalFree);
        var issued = 0;
        var results = new List<DownloadTicketResult>(items.Count);

        foreach (var item in items)
        {
            var target = await ResolveItemAsync(userId, item, ct);
            if (!target.Succeeded)
            {
                results.Add(new DownloadTicketResult(item.ItemId, null, target.Code, target.Message));
                continue;
            }

            if (issued >= capacity)
            {
                results.Add(new DownloadTicketResult(
                    item.ItemId,
                    null,
                    ErrorCodes.TooManyDownloads,
                    $"At most {limits.MaxConcurrentPerUser} downloads can run at once."));
                continue;
            }

            var ticket = Protect(new TicketPayload(userId, sessionId, target.Value!.FileId, target.Value.ChannelId, clock.UtcNow.ToUnixTimeMilliseconds()));
            results.Add(new DownloadTicketResult(item.ItemId, $"/api/v1/downloads/{ticket}", null, null));
            issued++;
        }

        return AppResult<DownloadTicketsResponse>.Ok(new DownloadTicketsResponse(results, limits.MaxConcurrentPerUser));
    }

    public async Task<AppResult<TicketedDownload>> ResolveAsync(
        Guid userId,
        Guid sessionId,
        string ticket,
        CancellationToken ct = default)
    {
        var payload = Unprotect(ticket);
        // Wrong user/session and unreadable or expired tickets look the same: not found.
        if (payload is null || payload.UserId != userId || payload.SessionId != sessionId)
        {
            return AppResult<TicketedDownload>.Fail(ErrorCodes.NotFound, "Download not found.");
        }

        AppResult<DownloadTarget> target;
        if (payload.ChannelId is Guid channelId)
        {
            var location = await db.Channels.AsNoTracking()
                .Where(c => c.Id == channelId)
                .Join(db.Teams.AsNoTracking(), c => c.TeamId, t => t.Id, (c, t) => new { t.Slug, c.Name })
                .FirstOrDefaultAsync(ct);
            if (location is null)
            {
                return AppResult<TicketedDownload>.Fail(ErrorCodes.NotFound, "Download not found.");
            }

            target = await files.ResolveDownloadAsync(userId, location.Slug, location.Name, payload.FileId, ct);
        }
        else
        {
            target = await personal.ResolveDownloadAsync(userId, payload.FileId, ct);
        }

        if (!target.Succeeded)
        {
            return AppResult<TicketedDownload>.Fail(ErrorCodes.NotFound, "Download not found.");
        }

        return AppResult<TicketedDownload>.Ok(
            new TicketedDownload(target.Value!, DateTimeOffset.FromUnixTimeMilliseconds(payload.IssuedAtMs)));
    }

    private Task<AppResult<DownloadTarget>> ResolveItemAsync(Guid userId, DownloadTicketItem item, CancellationToken ct)
    {
        if (string.Equals(item.Scope, "personal", StringComparison.OrdinalIgnoreCase))
        {
            return personal.ResolveDownloadAsync(userId, item.ItemId, ct);
        }

        if (string.Equals(item.Scope, "channel", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.TeamSlug)
            && !string.IsNullOrWhiteSpace(item.ChannelName))
        {
            return files.ResolveDownloadAsync(userId, item.TeamSlug, item.ChannelName, item.ItemId, ct);
        }

        return Task.FromResult(AppResult<DownloadTarget>.Fail(
            ErrorCodes.Validation,
            "Scope must be 'personal', or 'channel' with teamSlug and channelName."));
    }

    private string Protect(TicketPayload payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var lifetime = TimeSpan.FromHours(Math.Max(1, options.CurrentValue.TicketResumeHours));
        return Protector.Protect(json, lifetime);
    }

    private TicketPayload? Unprotect(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 2048)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TicketPayload>(Protector.Unprotect(ticket));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private sealed record TicketPayload(Guid UserId, Guid SessionId, Guid FileId, Guid? ChannelId, long IssuedAtMs);
}
