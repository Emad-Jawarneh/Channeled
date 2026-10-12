using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Identity;

public interface IAuthService
{
    Task<AppResult<AuthUserDto>> LoginAsync(LoginRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> LogoutAsync(Guid userId, Guid? sessionId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<AuthUserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<SessionDto>>> ListSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken ct = default);

    Task<AppResult> RevokeSessionAsync(Guid userId, Guid sessionId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<InvitationCreatedDto>> CreateInvitationAsync(CreateInvitationRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult<AuthUserDto>> AcceptInvitationAsync(AcceptInvitationRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult<AuthUserDto>> RegisterAsync(RegisterRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> ForgotPasswordAsync(ForgotPasswordRequest request, RequestContext context, CancellationToken ct = default);

    /// <summary>Raw reset token returned only for tests / email stub in M1.</summary>
    Task<AppResult<string>> ForgotPasswordForTestsAsync(ForgotPasswordRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> ResetPasswordAsync(ResetPasswordRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult<AuthUserDto>> SeedOperatorAsync(SeedOperatorRequest request, CancellationToken ct = default);
}
