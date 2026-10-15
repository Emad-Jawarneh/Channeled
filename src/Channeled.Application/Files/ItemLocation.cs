namespace Channeled.Application.Files;

/// <summary>
/// Where an item lives, for opening it directly (chat file chips, search hits, deep links) even when it would be
/// on a later page of its folder. <c>State</c> is <c>live</c> or <c>trashed</c>; <c>Scope</c> is <c>channel</c>
/// or <c>personal</c>. <c>FolderPath</c> runs root → parent. When trashed, <c>TrashEntryId</c> is the row that
/// appears in the trash list (the item itself or its outermost trashed ancestor folder).
/// </summary>
public sealed record ItemLocationDto(
    string State,
    string Scope,
    string? TeamSlug,
    string? ChannelName,
    Guid? FolderId,
    IReadOnlyList<FolderPathSegmentDto> FolderPath,
    FileItemDto Item,
    Guid? TrashEntryId);
