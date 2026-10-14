using System.Data;
using System.Security.Cryptography;
using System.Text;
using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Operations;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Identity;

public sealed class AccountMailService(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    IClock clock,
    ISelfServiceMail selfServiceMail,
    IEmailSender emailSender,
    IEmailSenderMarker senderMarker,
    IDataProtectionProvider dataProtection,
    IOptionsMonitor<OperationsOptions> operations,
    IOptionsMonitor<AccountOptions> accountOptions,
    IAuthStateChanged authStateChanged) : IAccountMailService
{
    public async Task<AppResult> StartEmailChangeAsync(
        Guid userId,
        StartEmailChangeRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!await selfServiceMail.IsAvailableAsync(ct))
        {
            return AppResult.Fail(ErrorCodes.Validation, "Mail is not configured.");
        }

        EmailAddress newEmail;
        try
        {
            newEmail = EmailAddress.Parse(request.NewEmail);
        }
        catch (ArgumentException ex)
        {
            return AppResult.Fail(ErrorCodes.Validation, ex.Message);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return AppResult.Fail(ErrorCodes.Unauthorized, "Invalid password.");
        }

        if (string.Equals(user.NormalizedEmail, newEmail.Value.ToUpperInvariant(), StringComparison.Ordinal))
        {
            return AppResult.Fail(ErrorCodes.Validation, "That is already your email.");
        }

        if (await IsEmailReservedOrTakenAsync(newEmail.Value, userId, ct))
        {
            return AppResult.Fail(ErrorCodes.Conflict, "That email is not available.");
        }

        var existing = await db.PendingEmailChanges
            .Where(p => p.UserId == userId && p.ConsumedAt == null && p.CancelledAt == null)
            .ToListAsync(ct);
        foreach (var row in existing)
        {
            row.CancelledAt = clock.UtcNow;
        }

        var confirmRaw = TokenHasher.CreateRawToken();
        var cancelRaw = TokenHasher.CreateRawToken();
        var opts = accountOptions.CurrentValue;
        var pending = new PendingEmailChange
        {
            Id = UuidV7.NewGuid(),
            UserId = userId,
            NewEmail = newEmail.Value,
            NewEmailNormalized = newEmail.Value.ToUpperInvariant(),
            ConfirmTokenHash = TokenHasher.Hash(confirmRaw),
            CancelTokenHash = TokenHasher.Hash(cancelRaw),
            CreatedAt = clock.UtcNow,
            ConfirmExpiresAt = clock.UtcNow.AddMinutes(opts.EmailChangePendingMinutes),
            CancelExpiresAt = clock.UtcNow.AddHours(opts.CancelTokenHours),
        };
        db.PendingEmailChanges.Add(pending);

        var publicUrl = operations.CurrentValue.PublicUrl.TrimEnd('/');
        var confirmLink = $"{publicUrl}/account/confirm-email#token={confirmRaw}";
        var cancelLink = $"{publicUrl}/account/security/email-change-cancel#token={cancelRaw}";

        EmailSendOutbox.Enqueue(
            db,
            dataProtection,
            clock.UtcNow,
            new EmailSendPlainPayload(
                newEmail.Value,
                "Confirm your new Channeled email",
                $"Confirm your email change: {confirmLink}\n\nIf the link fails, paste this token on the confirm page: {confirmRaw}",
                pending.ConfirmExpiresAt));

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            EmailSendOutbox.Enqueue(
                db,
                dataProtection,
                clock.UtcNow,
                new EmailSendPlainPayload(
                    user.Email,
                    "Channeled email change requested",
                    $"A change of your account email was requested. If this wasn't you: {cancelLink}\n\nPaste token if needed: {cancelRaw}",
                    pending.CancelExpiresAt));
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = userId,
            EventType = AuditEventType.EmailChangeStart,
            TargetType = "user",
            TargetId = userId.ToString(),
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> ConfirmEmailChangeAsync(
        ConfirmEmailChangeRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token required.");
        }

        var hash = TokenHasher.Hash(request.Token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var pending = await db.PendingEmailChanges
            .FirstOrDefaultAsync(p => p.ConfirmTokenHash == hash, ct);
        if (pending is null
            || pending.ConsumedAt is not null
            || pending.CancelledAt is not null
            || pending.ConfirmExpiresAt <= clock.UtcNow)
        {
            await tx.RollbackAsync(ct);
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        if (await IsEmailReservedOrTakenAsync(pending.NewEmail, pending.UserId, ct))
        {
            pending.CancelledAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AppResult.Fail(ErrorCodes.Conflict, "That email is not available.");
        }

        // Exactly one winner if two confirms race the same address (or token).
        var claimed = await db.PendingEmailChanges
            .Where(p => p.Id == pending.Id && p.ConsumedAt == null && p.CancelledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ConsumedAt, clock.UtcNow), ct);
        if (claimed != 1)
        {
            await tx.RollbackAsync(ct);
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        await db.Entry(pending).ReloadAsync(ct);

        var user = await userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
        {
            await tx.RollbackAsync(ct);
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        user.Email = pending.NewEmail;
        user.NormalizedEmail = pending.NewEmailNormalized;
        user.UserName = pending.NewEmail;
        user.NormalizedUserName = pending.NewEmailNormalized;
        user.EmailConfirmed = true;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return AppResult.Fail(ErrorCodes.Conflict, "That email is not available.");
        }

        var keep = context.CurrentSessionId;
        await SessionSecurity.RevokeSessionsAsync(db, userManager, authStateChanged, clock, user, keep, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = user.Id,
            EventType = AuditEventType.EmailChange,
            TargetType = "user",
            TargetId = user.Id.ToString(),
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> CancelEmailChangeAsync(
        CancelEmailChangeRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var pending = await FindByCancelTokenAsync(request.Token, ct);
        if (pending is null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        if (pending.ConsumedAt is null && pending.CancelledAt is null)
        {
            pending.CancelledAt = clock.UtcNow;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = pending.UserId,
            EventType = AuditEventType.EmailChangeCancel,
            TargetType = "user",
            TargetId = pending.UserId.ToString(),
            DetailsJson = """{"action":"cancel"}""",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> SignOutEverywhereWithCancelTokenAsync(
        CancelEmailChangeRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var pending = await FindByCancelTokenAsync(request.Token, ct);
        if (pending is null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        if (pending.ConsumedAt is null && pending.CancelledAt is null)
        {
            pending.CancelledAt = clock.UtcNow;
        }

        var user = await userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        await SessionSecurity.RevokeSessionsAsync(db, userManager, authStateChanged, clock, user, null, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> ChangePasswordWithCancelTokenAsync(
        CancelTokenPasswordChangeRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!PasswordRules.IsValid(request.NewPassword))
        {
            return AppResult.Fail(ErrorCodes.Validation, $"Password must be at least {PasswordRules.MinLength} characters.");
        }

        var pending = await FindByCancelTokenAsync(request.Token, ct);
        if (pending is null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        // Valid while pending or recently cancelled
        if (pending.ConsumedAt is not null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Token is invalid or expired.");
        }

        var user = await userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var reset = await userManager.RemovePasswordAsync(user);
        if (!reset.Succeeded)
        {
            return AppResult.Fail(ErrorCodes.Validation, string.Join("; ", reset.Errors.Select(e => e.Description)));
        }

        var add = await userManager.AddPasswordAsync(user, request.NewPassword);
        if (!add.Succeeded)
        {
            return AppResult.Fail(ErrorCodes.Validation, string.Join("; ", add.Errors.Select(e => e.Description)));
        }

        pending.CancelledAt ??= clock.UtcNow;
        // Consume cancel token uniqueness by rewriting hash
        pending.CancelTokenHash = TokenHasher.Hash(TokenHasher.CreateRawToken());
        await userManager.ResetAccessFailedCountAsync(user);
        await SessionSecurity.RevokeSessionsAsync(db, userManager, authStateChanged, clock, user, null, ct);
        await WriteAuditAsync(user.Id, AuditEventType.PasswordChange, user.Id.ToString(), context, ct, """{"via":"cancel_token"}""");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> AdminCorrectEmailAsync(
        Guid adminId,
        Guid targetUserId,
        AdminCorrectEmailRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var admin = await userManager.FindByIdAsync(adminId.ToString());
        if (admin is null || !admin.IsPlatformAdmin)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "Platform admin required.");
        }

        if (!await userManager.CheckPasswordAsync(admin, request.AdminPassword))
        {
            return AppResult.Fail(ErrorCodes.Unauthorized, "Invalid password.");
        }

        var target = await userManager.FindByIdAsync(targetUserId.ToString());
        if (target is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        if (target.IsPlatformAdmin && target.Id != adminId)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "Cannot change another platform admin's email via API.");
        }

        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException ex)
        {
            return AppResult.Fail(ErrorCodes.Validation, ex.Message);
        }

        if (await IsEmailReservedOrTakenAsync(email.Value, targetUserId, ct))
        {
            return AppResult.Fail(ErrorCodes.Conflict, "That email is not available.");
        }

        var oldEmail = target.Email;
        target.Email = email.Value;
        target.NormalizedEmail = email.Value.ToUpperInvariant();
        target.UserName = email.Value;
        target.NormalizedUserName = email.Value.ToUpperInvariant();
        target.EmailConfirmed = true;
        await userManager.UpdateAsync(target);
        await SessionSecurity.RevokeSessionsAsync(db, userManager, authStateChanged, clock, target, null, ct);

        if (!string.IsNullOrWhiteSpace(oldEmail) && senderMarker.IsSmtp)
        {
            EmailSendOutbox.Enqueue(
                db,
                dataProtection,
                clock.UtcNow,
                new EmailSendPlainPayload(
                    oldEmail,
                    "Your Channeled email was changed by an administrator",
                    "An administrator changed the email on your account. Contact support if this was unexpected.",
                    null));
        }

        await WriteAuditAsync(adminId, AuditEventType.AdminEmailCorrection, targetUserId.ToString(), context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<DateTimeOffset?>> TestSmtpAsync(
        Guid adminId,
        string toEmail,
        RequestContext context,
        CancellationToken ct = default)
    {
        var admin = await userManager.FindByIdAsync(adminId.ToString());
        if (admin is null || !admin.IsPlatformAdmin)
        {
            return AppResult<DateTimeOffset?>.Fail(ErrorCodes.Forbidden, "Platform admin required.");
        }

        if (!senderMarker.IsSmtp)
        {
            return AppResult<DateTimeOffset?>.Fail(ErrorCodes.Validation, "SMTP sender is not active.");
        }

        try
        {
            await emailSender.SendAsync(toEmail, "Channeled SMTP test", "SMTP probe succeeded.", ct);
        }
        catch (Exception ex)
        {
            return AppResult<DateTimeOffset?>.Fail(ErrorCodes.Validation, ex.Message);
        }

        var settings = await db.OperationsSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new Operations.OperationsSettingsEntity { Id = 1 };
            db.OperationsSettings.Add(settings);
        }

        var o = operations.CurrentValue;
        settings.SmtpVerifiedAt = clock.UtcNow;
        settings.SmtpSettingsFingerprint = Fingerprint(o);
        await WriteAuditAsync(adminId, AuditEventType.SmtpTest, "operations", "smtp", context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<DateTimeOffset?>.Ok(settings.SmtpVerifiedAt);
    }

    private async Task<PendingEmailChange?> FindByCancelTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = TokenHasher.Hash(token);
        var pending = await db.PendingEmailChanges.FirstOrDefaultAsync(p => p.CancelTokenHash == hash, ct);
        if (pending is null || pending.CancelExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        // Allow while pending or recently cancelled (within cancel token lifetime)
        if (pending.ConsumedAt is not null)
        {
            return null;
        }

        return pending;
    }

    private async Task<bool> IsEmailReservedOrTakenAsync(string email, Guid exceptUserId, CancellationToken ct)
    {
        var normalized = email.ToUpperInvariant();
        if (await userManager.Users.AsNoTracking()
                .AnyAsync(u => u.NormalizedEmail == normalized && u.Id != exceptUserId, ct))
        {
            return true;
        }

        return await db.PendingEmailChanges.AsNoTracking()
            .AnyAsync(
                p => p.NewEmailNormalized == normalized
                     && p.UserId != exceptUserId
                     && p.ConsumedAt == null
                     && p.CancelledAt == null
                     && p.ConfirmExpiresAt > clock.UtcNow,
                ct);
    }

    private async Task WriteAuditAsync(
        Guid actorId,
        AuditEventType type,
        string targetId,
        RequestContext context,
        CancellationToken ct,
        string details = "{}") =>
        await WriteAuditAsync(actorId, type, "user", targetId, context, ct, details);

    private async Task WriteAuditAsync(
        Guid actorId,
        AuditEventType type,
        string targetType,
        string targetId,
        RequestContext context,
        CancellationToken ct,
        string detailsJson = "{}")
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            EventType = type,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = detailsJson,
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    public static string Fingerprint(OperationsOptions o) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{o.SmtpHost}|{o.SmtpPort}|{o.SmtpFrom}|{o.SmtpUsername}|{o.SmtpUseStartTls}"))).ToLowerInvariant();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
