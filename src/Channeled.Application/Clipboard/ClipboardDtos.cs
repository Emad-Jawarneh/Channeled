namespace Channeled.Application.Clipboard;

public sealed record ClipboardScopeRequest(
    string Scope,
    string? TeamSlug,
    string? ChannelName,
    Guid? FolderId);

public sealed record ClipboardPasteRequest(
    string Mode,
    IReadOnlyList<Guid> ItemIds,
    ClipboardScopeRequest Source,
    ClipboardScopeRequest Target);

public sealed record ClipboardPasteItemResult(
    Guid ItemId,
    string Status,
    Guid? NewItemId,
    string? ErrorCode,
    string? Message);

public sealed record ClipboardPasteResponse(
    int Succeeded,
    int Failed,
    IReadOnlyList<ClipboardPasteItemResult> Results);
