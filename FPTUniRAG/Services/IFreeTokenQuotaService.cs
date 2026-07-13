namespace FPTUniRAG.Services;

public interface IFreeTokenQuotaService
{
    const long DefaultMonthlyTokenLimit = 2000;

    Task<long> GetMonthlyTokenLimitAsync(CancellationToken cancellationToken = default);

    Task<long> UpdateMonthlyTokenLimitAsync(
        long monthlyTokenLimit,
        Guid updatedBy,
        CancellationToken cancellationToken = default);
}
