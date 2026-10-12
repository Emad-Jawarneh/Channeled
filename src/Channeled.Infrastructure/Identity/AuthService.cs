using System.Text.Json;
using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ApplicationDbContext db,
    IClock clock,
    IEmailSender emailSender) : IAuthService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    public async Task<AppResult<AuthUserDto>> LoginAsync(
        LoginRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Unauthorized, "Invalid email or password.");
        }

        var user = await userManager.FindByEmailAsync(email.Value);
        // Constant-ish response for unknown users
        if (user is null || user.Status != "active")
        {
            await WriteAuditAsync(null, AuditEventType.LoginFailure, "user", email.Value, context, ct);
            await db.SaveChangesAsync(ct);
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Unauthorized, "Invalid email or password.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await WriteAuditAsync(user.Id, AuditEventType.LoginFailure, "user", user.Id.ToString(), context, ct, """{"reason":"locked_out"}""");
            await db.SaveChangesAsync(ct);
            return AppResult<AuthUserDto>.Fail(ErrorCodes.LockedOut, "Account temporarily locked.");
        }

        var passwordOk = await userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordOk)
        {
            await userManager.AccessFailedAsync(user);
            await WriteAuditAsync(user.Id, AuditEventType.LoginFailure, "user", user.Id.ToString(), context, ct);
            await db.SaveChangesAsync(ct);

            if (await userManager.IsLockedOutAsync(user))
            {
                return AppResult<AuthUserDto>.Fail(ErrorCodes.LockedOut, "Account temporarily locked.");
            }

            return AppResult<AuthUserDto>.Fail(ErrorCodes.Unauthorized, "Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var session = new UserSession
        {
            Id = UuidV7.NewGuid(),
            UserId = user.Id,
            CreatedAt = clock.UtcNow,
            LastSeenAt = clock.UtcNow,
            Ip = context.Ip,
            UserAgent = Truncate(context.UserAgent, 512),
        };
        db.UserSessions.Add(session);

        user.LastLoginAt = clock.UtcNow;
        await userManager.UpdateAsync(user);

        var extraClaims = new List<System.Security.Claims.Claim>
        {
            new("sid", session.Id.ToString("D")),
        };
        if (user.IsPlatformAdmin)
        {
            extraClaims.Add(new System.Security.Claims.Claim("is_platform_admin", "true"));
        }

        await signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

        await WriteAuditAsync(user.Id, AuditEventType.LoginSuccess, "user", user.Id.ToString(), context, ct);
        await db.SaveChangesAsync(ct);

        return AppResult<AuthUserDto>.Ok(ToDto(user));
    }

    public async Task<AppResult> LogoutAsync(
        Guid userId,
        Guid? sessionId,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (sessionId is Guid sid)
        {
            var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid && s.UserId == userId, ct);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = clock.UtcNow;
            }
        }

        await signInManager.SignOutAsync();
        await WriteAuditAsync(userId, AuditEventType.Logout, "user", userId.ToString(), context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<AuthUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.Status != "active")
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Unauthorized, "Not authenticated.");
        }

        return AppResult<AuthUserDto>.Ok(ToDto(user));
    }

    public async Task<AppResult<IReadOnlyList<SessionDto>>> ListSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken ct = default)
    {
        var sessions = await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(ct);

        IReadOnlyList<SessionDto> dtos = sessions
            .Select(s => new SessionDto(
                s.Id,
                s.CreatedAt,
                s.LastSeenAt,
                s.Ip,
                s.UserAgent,
                currentSessionId == s.Id))
            .ToList();

        return AppResult<IReadOnlyList<SessionDto>>.Ok(dtos);
    }

    public async Task<AppResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Session not found.");
        }

        if (session.RevokedAt is null)
        {
            session.RevokedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.SessionRevoke, "session", sessionId.ToString(), context, ct);
            await db.SaveChangesAsync(ct);
        }

        return AppResult.Ok();
    }

    public async Task<AppResult<InvitationCreatedDto>> CreateInvitationAsync(
        CreateInvitationRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException ex)
        {
            return AppResult<InvitationCreatedDto>.Fail(ErrorCodes.Validation, ex.Message);
        }

        var existing = await userManager.FindByEmailAsync(email.Value);
        if (existing is not null)
        {
            return AppResult<InvitationCreatedDto>.Fail(ErrorCodes.Conflict, "User already exists.");
        }

        var raw = TokenHasher.CreateRawToken();
        var invitation = new PlatformInvitation
        {
            Id = UuidV7.NewGuid(),
            Email = email.Value,
            TokenHash = TokenHasher.Hash(raw),
            InvitedByUserId = request.InvitedByUserId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.Add(InvitationLifetime),
        };
        db.PlatformInvitations.Add(invitation);
        await WriteAuditAsync(request.InvitedByUserId, AuditEventType.InviteCreate, "invitation", invitation.Id.ToString(), context, ct);
        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            email.Value,
            "Channeled invitation",
            $"Use this invitation token to create your account: {raw}",
            ct);

        return AppResult<InvitationCreatedDto>.Ok(new InvitationCreatedDto(invitation.Id, raw, invitation.ExpiresAt));
    }

    public async Task<AppResult<AuthUserDto>> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!PasswordRules.IsValid(request.Password))
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, $"Password must be at least {PasswordRules.MinLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, "Display name is required.");
        }

        var hash = TokenHasher.Hash(request.Token);
        var invitation = await db.PlatformInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null || invitation.RevokedAt is not null || invitation.AcceptedAt is not null)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.InvalidToken, "Invitation token is invalid.");
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.InvalidToken, "Invitation token has expired.");
        }

        var user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = invitation.Email,
            UserName = invitation.Email,
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true,
            Status = "active",
        };

        var create = await userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        invitation.AcceptedAt = clock.UtcNow;
        await WriteAuditAsync(user.Id, AuditEventType.InviteAccept, "invitation", invitation.Id.ToString(), context, ct);
        await WriteAuditAsync(user.Id, AuditEventType.UserCreate, "user", user.Id.ToString(), context, ct);
        await db.SaveChangesAsync(ct);

        return AppResult<AuthUserDto>.Ok(ToDto(user));
    }

    public async Task<AppResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var result = await CreatePasswordResetAsync(request, context, ct);
        // Always succeed to avoid account enumeration
        return AppResult.Ok();
    }

    public async Task<AppResult<string>> ForgotPasswordForTestsAsync(
        ForgotPasswordRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var raw = await CreatePasswordResetAsync(request, context, ct);
        return AppResult<string>.Ok(raw ?? "");
    }

    public async Task<AppResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!PasswordRules.IsValid(request.NewPassword))
        {
            return AppResult.Fail(ErrorCodes.Validation, $"Password must be at least {PasswordRules.MinLength} characters.");
        }

        var hash = TokenHasher.Hash(request.Token);
        var token = await db.PasswordResetTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.UsedAt is not null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Reset token is invalid.");
        }

        if (token.ExpiresAt <= clock.UtcNow)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Reset token has expired.");
        }

        var user = token.User ?? await userManager.FindByIdAsync(token.UserId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.InvalidToken, "Reset token is invalid.");
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!reset.Succeeded)
        {
            return AppResult.Fail(ErrorCodes.Validation, string.Join("; ", reset.Errors.Select(e => e.Description)));
        }

        token.UsedAt = clock.UtcNow;
        await userManager.UpdateSecurityStampAsync(user);
        await WriteAuditAsync(user.Id, AuditEventType.PasswordReset, "user", user.Id.ToString(), context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!PasswordRules.IsValid(request.NewPassword))
        {
            return AppResult.Fail(ErrorCodes.Validation, $"Password must be at least {PasswordRules.MinLength} characters.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "User not found.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return AppResult.Fail(ErrorCodes.Validation, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await WriteAuditAsync(userId, AuditEventType.PasswordChange, "user", userId.ToString(), context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<AuthUserDto>> SeedOperatorAsync(SeedOperatorRequest request, CancellationToken ct = default)
    {
        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException ex)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, ex.Message);
        }

        if (!PasswordRules.IsValid(request.Password))
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, $"Password must be at least {PasswordRules.MinLength} characters.");
        }

        if (await userManager.Users.AnyAsync(u => u.IsPlatformAdmin, ct))
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Conflict, "Operator already seeded.");
        }

        var user = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = email.Value,
            UserName = email.Value,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Operator" : request.DisplayName.Trim(),
            EmailConfirmed = true,
            IsPlatformAdmin = true,
            Status = "active",
        };

        var create = await userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return AppResult<AuthUserDto>.Fail(ErrorCodes.Validation, string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        await WriteAuditAsync(
            user.Id,
            AuditEventType.UserCreate,
            "user",
            user.Id.ToString(),
            new RequestContext("seed", "seed"),
            ct,
            """{"role":"platform_admin"}""");
        await db.SaveChangesAsync(ct);
        return AppResult<AuthUserDto>.Ok(ToDto(user));
    }

    private async Task<string?> CreatePasswordResetAsync(
        ForgotPasswordRequest request,
        RequestContext context,
        CancellationToken ct)
    {
        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var user = await userManager.FindByEmailAsync(email.Value);
        if (user is null || user.Status != "active")
        {
            return null;
        }

        var raw = TokenHasher.CreateRawToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = UuidV7.NewGuid(),
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(raw),
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.Add(PasswordResetLifetime),
        });
        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            email.Value,
            "Channeled password reset",
            $"Use this reset token: {raw}",
            ct);

        return raw;
    }

    private async Task WriteAuditAsync(
        Guid? actorId,
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

    private static AuthUserDto ToDto(AppUser user) =>
        new(user.Id, user.Email ?? "", user.DisplayName, user.IsPlatformAdmin);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
