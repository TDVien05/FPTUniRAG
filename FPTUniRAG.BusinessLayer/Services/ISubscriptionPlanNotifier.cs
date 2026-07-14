namespace FPTUniRAG.BusinessLayer.Services;

public interface ISubscriptionPlanNotifier
{
    Task NotifyPlanCreatedAsync(CancellationToken cancellationToken = default);

    Task NotifyPlanUpdatedAsync(CancellationToken cancellationToken = default);

    Task NotifyPlanDeletedAsync(CancellationToken cancellationToken = default);

    Task NotifyFreeTokenLimitUpdatedAsync(CancellationToken cancellationToken = default);
}
