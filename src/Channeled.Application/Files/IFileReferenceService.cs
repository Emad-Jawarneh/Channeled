using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Files;

public interface IFileReferenceService
{
    /// <summary>
    /// Validates file ids for posting in a team text channel: same team, team files only, poster can browse each channel.
    /// Returns de-duplicated ids in first-seen order (max <see cref="Chat.ChatOptions.MaxFileRefsPerMessage"/>).
    /// </summary>
    Task<AppResult<IReadOnlyList<Guid>>> ValidateForMessagePostAsync(
        Guid userId,
        Guid teamId,
        IReadOnlyList<Guid>? fileIds,
        CancellationToken ct = default);

    Task<AppResult<ResolveFilesResponse>> ResolveAsync(
        Guid userId,
        string teamSlug,
        ResolveFilesRequest request,
        CancellationToken ct = default);
}
