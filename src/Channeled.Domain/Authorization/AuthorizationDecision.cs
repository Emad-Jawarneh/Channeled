namespace Channeled.Domain.Authorization;

public enum AuthorizationOutcome
{
    Allow,
    Deny,
}

public sealed record AuthorizationDecision(
    AuthorizationOutcome Outcome,
    string ReasonCode)
{
    public bool Allowed => Outcome == AuthorizationOutcome.Allow;

    public static AuthorizationDecision Allow() =>
        new(AuthorizationOutcome.Allow, "allow");

    public static AuthorizationDecision Deny(string reasonCode) =>
        new(AuthorizationOutcome.Deny, reasonCode);
}
