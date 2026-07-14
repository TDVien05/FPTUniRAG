using FPTUniRAG.DataAccessLayer.Repositories.Subscriptions;

namespace FPTUniRAG.BusinessLayer.Subscriptions;

public sealed class StudentPlanService(ISubscriptionRepository repository, IFreeTokenQuotaService freeQuota) : IStudentPlanService
{
    public async Task<StudentPlanStateDto> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var plans = await repository.GetActivePlansAsync(cancellationToken);
        var active = await repository.GetActiveSubscriptionAsync(userId, now, cancellationToken);
        var usage = await repository.GetMonthlyUsageAsync(userId, cancellationToken);
        var canReplace = active?.Plan.MonthlyTokenLimit is > 0 && Math.Max(0, active.Plan.MonthlyTokenLimit.Value - usage) <= 0;
        var current = active is null
            ? new StudentCurrentPlanDto(null, "free", "Free", 0, await freeQuota.GetMonthlyTokenLimitAsync(cancellationToken), null, null)
            : new StudentCurrentPlanDto(active.PlanId, active.Plan.PlanCode, active.Plan.PlanName, active.Plan.MonthlyPrice, active.Plan.MonthlyTokenLimit, active.StartedAt, active.ExpiresAt);
        return new(plans.Select(p => new StudentPlanDto(p.PlanId, p.PlanCode, p.PlanName, p.Description, p.MonthlyPrice, p.MonthlyTokenLimit ?? 0, p.HasAdvancedModels, p.HasPrioritySupport, p.HasFileUpload, p.HasHistoryExport)).ToList(), current, active is not null, usage, canReplace);
    }
    public async Task<bool> CanPurchaseAsync(Guid userId, CancellationToken cancellationToken = default)
    { var state = await GetStateAsync(userId, cancellationToken); return !state.HasActiveSubscription || state.CanReplace; }
}
