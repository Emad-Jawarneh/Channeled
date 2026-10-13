using Channeled.Application.Abstractions.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Channeled.Api.Realtime;

/// <summary>
/// Requires Origin on cookie-authenticated hub connections and allow-lists configured public origins.
/// </summary>
public sealed class OriginHubFilter(IOptions<RealtimeOptions> options) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        EnsureOrigin(invocationContext.Context);
        return await next(invocationContext);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        EnsureOrigin(context.Context);
        return next(context);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next) =>
        next(context, exception);

    private void EnsureOrigin(HubCallerContext context)
    {
        var http = context.GetHttpContext();
        if (http is null)
        {
            throw new HubException("origin_required");
        }

        var origin = http.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new HubException("origin_required");
        }

        var allowed = options.Value.AllowedOrigins;
        if (!allowed.Any(a => string.Equals(a, origin, StringComparison.OrdinalIgnoreCase)))
        {
            throw new HubException("origin_rejected");
        }
    }
}
