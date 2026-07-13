namespace FPTUniRAG.Services;

public interface ISubscriptionPlanNotifier
{
    Task NotifyPlanCreatedAsync(CancellationToken cancellationToken = default);

    Task NotifyPlanDeletedAsync(CancellationToken cancellationToken = default);
}
