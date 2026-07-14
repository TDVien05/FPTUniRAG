using FPTUniRAG.BusinessLayer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class SignalRSubscriptionPlanNotifier : ISubscriptionPlanNotifier
{
    private readonly IHubContext<SubscriptionPlanHub> _hubContext;

    public SignalRSubscriptionPlanNotifier(IHubContext<SubscriptionPlanHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyPlanCreatedAsync(CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync(
            SubscriptionPlanHub.PlanCreatedEvent,
            cancellationToken);
    }

    public Task NotifyPlanDeletedAsync(CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync(
            SubscriptionPlanHub.PlanDeletedEvent,
            cancellationToken);
    }

    public Task NotifyPlanUpdatedAsync(CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync(
            SubscriptionPlanHub.PlanUpdatedEvent,
            cancellationToken);
    }

    public Task NotifyFreeTokenLimitUpdatedAsync(CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.All.SendAsync(
            SubscriptionPlanHub.FreeTokenLimitUpdatedEvent,
            cancellationToken);
    }
}
