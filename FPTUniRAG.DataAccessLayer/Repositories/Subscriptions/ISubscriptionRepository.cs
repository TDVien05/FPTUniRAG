namespace FPTUniRAG.DataAccessLayer.Repositories.Subscriptions;

public interface ISubscriptionRepository
{
    Task<long?> GetFreeMonthlyTokenLimitAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertFreeMonthlyTokenLimitAsync(long limit, Guid updatedBy, DateTime updatedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default);
    Task<FPTUniRAG.DataAccessLayer.Entities.StudentSubscription?> GetActiveSubscriptionAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);
    Task<decimal> GetMonthlyUsageAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlanAdminRecord>> GetAdminPlansAsync(CancellationToken cancellationToken = default);
    Task<bool> PlanCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);
    Task<FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default);
    Task AddPlanAsync(FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan plan, CancellationToken cancellationToken = default);
    Task SavePlanAsync(FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan plan, CancellationToken cancellationToken = default);
    Task<bool> DeletePlanAsync(FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan plan, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPlanAdminRecord(FPTUniRAG.DataAccessLayer.Entities.SubscriptionPlan Plan, bool HasStudentSubscriptions, bool HasTokenUsageLogs);
