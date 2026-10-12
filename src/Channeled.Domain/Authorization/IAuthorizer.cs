namespace Channeled.Domain.Authorization;

public interface IAuthorizer
{
    AuthorizationDecision Authorize(AuthorizationContext context);
}
