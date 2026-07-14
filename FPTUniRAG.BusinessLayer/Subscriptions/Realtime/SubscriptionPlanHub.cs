using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.BusinessLayer.Subscriptions.Realtime;

[Authorize(Policy = "StudentOrAdmin")]
public sealed class SubscriptionPlanHub : Hub
{
    public const string PlanCreatedEvent = "subscriptionPlanCreated";
    public const string PlanUpdatedEvent = "subscriptionPlanUpdated";
    public const string PlanDeletedEvent = "subscriptionPlanDeleted";
    public const string FreeTokenLimitUpdatedEvent = "freeTokenLimitUpdated";
}
