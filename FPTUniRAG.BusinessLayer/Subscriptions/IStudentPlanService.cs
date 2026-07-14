namespace FPTUniRAG.BusinessLayer.Subscriptions;

public interface IStudentPlanService
{
    Task<StudentPlanStateDto> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> CanPurchaseAsync(Guid userId, CancellationToken cancellationToken = default);
}
public sealed record StudentPlanStateDto(IReadOnlyList<StudentPlanDto> Plans, StudentCurrentPlanDto CurrentPlan, bool HasActiveSubscription, decimal TokensUsedThisMonth, bool CanReplace);
public sealed record StudentPlanDto(Guid PlanId, string PlanCode, string PlanName, string? Description, decimal MonthlyPrice, long MonthlyTokenLimit, bool HasAdvancedModels, bool HasPrioritySupport, bool HasFileUpload, bool HasHistoryExport);
public sealed record StudentCurrentPlanDto(Guid? PlanId, string PlanCode, string PlanName, decimal MonthlyPrice, long? MonthlyTokenLimit, DateTime? StartedAt, DateTime? ExpiresAt);
