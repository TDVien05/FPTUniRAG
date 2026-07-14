namespace FPTUniRAG.BusinessLayer.Subscriptions;

public interface ISubscriptionPlanManagementService
{
    Task<IReadOnlyList<ManagedSubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> CreateAsync(SubscriptionPlanCommand command, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> UpdateAsync(Guid planId, SubscriptionPlanCommand command, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> DeleteAsync(Guid planId, CancellationToken cancellationToken = default);
}
public sealed record SubscriptionPlanCommand(string PlanCode, string PlanName, string? Description, decimal MonthlyPrice, long MonthlyTokenLimit, bool IsActive);
public sealed record ManagedSubscriptionPlanDto(Guid PlanId, string PlanCode, string PlanName, string? Description, decimal MonthlyPrice, long MonthlyTokenLimit, bool IsActive, bool HasAdvancedModels, bool HasPrioritySupport, bool HasFileUpload, bool HasHistoryExport, bool HasStudentSubscriptions, bool HasTokenUsageLogs);
