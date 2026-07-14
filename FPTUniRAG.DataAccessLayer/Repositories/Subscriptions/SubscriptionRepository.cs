using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Subscriptions;

public sealed class SubscriptionRepository(AppDbContext context) : ISubscriptionRepository
{
    public async Task<IReadOnlyList<SubscriptionPlan>> GetActivePlansAsync(CancellationToken token = default) => await context.SubscriptionPlans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice).ThenBy(p => p.PlanName).ToListAsync(token);
    public Task<StudentSubscription?> GetActiveSubscriptionAsync(Guid userId, DateTime now, CancellationToken token = default) => context.StudentSubscriptions.AsNoTracking().Include(s => s.Plan).FirstOrDefaultAsync(s => s.UserId == userId && s.SubscriptionStatus == "active" && (s.ExpiresAt == null || s.ExpiresAt > now), token);
    public async Task<decimal> GetMonthlyUsageAsync(Guid userId, CancellationToken token = default) => (await context.StudentTokenUsageCurrentMonths.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, token))?.TotalTokensUsedThisMonth ?? 0;
    public async Task<IReadOnlyList<SubscriptionPlanAdminRecord>> GetAdminPlansAsync(CancellationToken token = default) => await context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.MonthlyPrice).ThenBy(p => p.PlanName).Select(p => new SubscriptionPlanAdminRecord(p, p.StudentSubscriptions.Any(), p.TokenUsageLogs.Any())).ToListAsync(token);
    public Task<bool> PlanCodeExistsAsync(string code, Guid? excludeId, CancellationToken token = default) => context.SubscriptionPlans.AnyAsync(p => p.PlanCode == code && (!excludeId.HasValue || p.PlanId != excludeId), token);
    public Task<SubscriptionPlan?> FindPlanAsync(Guid id, CancellationToken token = default) => context.SubscriptionPlans.SingleOrDefaultAsync(p => p.PlanId == id, token);
    public async Task AddPlanAsync(SubscriptionPlan plan, CancellationToken token = default) { context.SubscriptionPlans.Add(plan); await context.SaveChangesAsync(token); }
    public Task SavePlanAsync(SubscriptionPlan plan, CancellationToken token = default) => context.SaveChangesAsync(token);
    public async Task<bool> DeletePlanAsync(SubscriptionPlan plan, CancellationToken token = default) { if (await context.StudentSubscriptions.AnyAsync(s => s.PlanId == plan.PlanId, token) || await context.TokenUsageLogs.AnyAsync(l => l.PlanId == plan.PlanId, token)) return false; context.SubscriptionPlans.Remove(plan); await context.SaveChangesAsync(token); return true; }
    public async Task<long?> GetFreeMonthlyTokenLimitAsync(CancellationToken cancellationToken = default) =>
        (await context.StudentFreeQuotaSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken))?.MonthlyTokenLimit;

    public async Task<long> UpsertFreeMonthlyTokenLimitAsync(long limit, Guid updatedBy, DateTime updatedAt, CancellationToken cancellationToken = default)
    {
        var setting = await context.StudentFreeQuotaSettings.SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);
        if (setting is null)
        {
            setting = new StudentFreeQuotaSetting { SettingId = 1 };
            context.StudentFreeQuotaSettings.Add(setting);
        }
        setting.MonthlyTokenLimit = limit;
        setting.UpdatedAt = updatedAt;
        setting.UpdatedBy = updatedBy;
        await context.SaveChangesAsync(cancellationToken);
        return setting.MonthlyTokenLimit;
    }
}
