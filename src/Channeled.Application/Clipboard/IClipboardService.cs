using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.Clipboard;

public interface IClipboardService
{
    Task<AppResult<ClipboardPasteResponse>> PasteAsync(
        Guid userId,
        ClipboardPasteRequest request,
        RequestContext context,
        CancellationToken ct = default);
}
