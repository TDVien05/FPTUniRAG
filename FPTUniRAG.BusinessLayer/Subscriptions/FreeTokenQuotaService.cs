using FPTUniRAG.DataAccessLayer.Repositories.Subscriptions;

namespace FPTUniRAG.BusinessLayer.Subscriptions;

public sealed class FreeTokenQuotaService : IFreeTokenQuotaService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ILogger<FreeTokenQuotaService> _logger;

    public FreeTokenQuotaService(ISubscriptionRepository subscriptionRepository, ILogger<FreeTokenQuotaService> logger)
    {
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<long> GetMonthlyTokenLimitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var limit = await _subscriptionRepository.GetFreeMonthlyTokenLimitAsync(cancellationToken);
            return limit > 0
                ? limit.Value
                : IFreeTokenQuotaService.DefaultMonthlyTokenLimit;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load free student token limit. Using default {DefaultLimit}.", IFreeTokenQuotaService.DefaultMonthlyTokenLimit);
            return IFreeTokenQuotaService.DefaultMonthlyTokenLimit;
        }
    }

    public async Task<long> UpdateMonthlyTokenLimitAsync(
        long monthlyTokenLimit,
        Guid updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (monthlyTokenLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyTokenLimit), "Free monthly token limit must be greater than zero.");
        }

        return await _subscriptionRepository.UpsertFreeMonthlyTokenLimitAsync(
            monthlyTokenLimit, updatedBy, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), cancellationToken);
    }
}
