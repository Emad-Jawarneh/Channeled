using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.Chat;

public interface IChatService
{
    Task<AppResult<ChannelMessageDto>> PostMessageAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        PostMessageRequest request,
        CancellationToken ct = default);

    Task<AppResult<MessageListResponse>> ListMessagesAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        ListMessagesQuery query,
        CancellationToken ct = default);

    /// <summary>Soft-deletes (and scrubs) a message; the author or an Admin+ may delete.</summary>
    Task<AppResult> DeleteMessageAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid messageId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<PresenceListResponse>> ListPresenceAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        CancellationToken ct = default);
}

public interface IChatPurgeService
{
    /// <summary>Hard-deletes soft-deleted messages whose deletion is older than the retention window.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
