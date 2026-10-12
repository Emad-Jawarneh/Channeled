namespace Channeled.Domain.Audit;

public enum AuditEventType
{
    LoginSuccess = 1,
    LoginFailure = 2,
    Logout = 3,
    PasswordChange = 4,
    PasswordReset = 5,
    SessionRevoke = 6,
    UserCreate = 7,
    UserDisable = 8,
    InviteCreate = 14,
    InviteAccept = 15,
    InviteRevoke = 16,
}
