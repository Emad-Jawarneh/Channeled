using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Identity;

public interface IAccountMailService
{
    Task<AppResult> StartEmailChangeAsync(Guid userId, StartEmailChangeRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> CancelEmailChangeAsync(CancelEmailChangeRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> SignOutEverywhereWithCancelTokenAsync(CancelEmailChangeRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> ChangePasswordWithCancelTokenAsync(CancelTokenPasswordChangeRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult> AdminCorrectEmailAsync(Guid adminId, Guid targetUserId, AdminCorrectEmailRequest request, RequestContext context, CancellationToken ct = default);

    Task<AppResult<DateTimeOffset?>> TestSmtpAsync(Guid adminId, string toEmail, RequestContext context, CancellationToken ct = default);
}
