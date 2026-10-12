using Channeled.Application.Abstractions.Results;

namespace Channeled.Application.Search;

public interface ISearchService
{
    Task<AppResult<SearchResponse>> SearchAsync(Guid userId, SearchQuery query, CancellationToken ct = default);
}
