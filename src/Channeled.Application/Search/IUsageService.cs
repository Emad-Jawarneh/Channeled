using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Search;

public interface IUsageService
{
    Task<AppResult<TeamStorageDto>> GetTeamStorageAsync(Guid userId, string teamSlug, CancellationToken ct = default);
}
