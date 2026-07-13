using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.Hubs;

[Authorize(Policy = "StudentOrAdmin")]
public sealed class SubscriptionPlanHub : Hub
{
    public const string PlanCreatedEvent = "subscriptionPlanCreated";
    public const string PlanDeletedEvent = "subscriptionPlanDeleted";
}
