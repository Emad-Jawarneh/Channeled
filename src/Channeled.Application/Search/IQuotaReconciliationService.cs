namespace Channeled.Application.Search;

public interface IQuotaReconciliationService
{
    Task<QuotaReconciliationResult> ReconcileAsync(CancellationToken ct = default);
}
