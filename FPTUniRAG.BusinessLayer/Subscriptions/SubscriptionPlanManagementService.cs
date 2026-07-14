using FPTUniRAG.BusinessLayer.Payments.Stripe;
using FPTUniRAG.BusinessLayer.Subscriptions.Realtime;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.DataAccessLayer.Repositories.Subscriptions;

namespace FPTUniRAG.BusinessLayer.Subscriptions;

public sealed class SubscriptionPlanManagementService(ISubscriptionRepository repository, IStripePaymentService stripe, ISubscriptionPlanNotifier notifier) : ISubscriptionPlanManagementService
{
    public async Task<IReadOnlyList<ManagedSubscriptionPlanDto>> GetPlansAsync(CancellationToken token = default) =>
        (await repository.GetAdminPlansAsync(token)).Select(x => new ManagedSubscriptionPlanDto(x.Plan.PlanId, x.Plan.PlanCode, x.Plan.PlanName, x.Plan.Description, x.Plan.MonthlyPrice,
            x.Plan.MonthlyTokenLimit ?? 0, x.Plan.IsActive, x.Plan.HasAdvancedModels, x.Plan.HasPrioritySupport, x.Plan.HasFileUpload, x.Plan.HasHistoryExport, x.HasStudentSubscriptions, x.HasTokenUsageLogs)).ToList();

    public async Task<(bool Success, string Message)> CreateAsync(SubscriptionPlanCommand command, CancellationToken token = default)
    {
        var validation = Validate(command); if (validation is not null) return (false, validation);
        var code = NormalizeCode(command.PlanCode); if (await repository.PlanCodeExistsAsync(code, null, token)) return (false, "That plan code already exists.");
        var plan = new SubscriptionPlan { PlanId = Guid.NewGuid(), PlanCode = code, PlanName = command.PlanName.Trim(), Description = Optional(command.Description), MonthlyPrice = command.MonthlyPrice,
            MonthlyTokenLimit = command.MonthlyTokenLimit, HasUnlimitedChat = false, HasFileUpload = true, IsActive = command.IsActive };
        var price = await stripe.EnsurePlanPriceAsync(plan.PlanId, plan.PlanCode, plan.PlanName, plan.Description, plan.MonthlyPrice, null, token);
        if (!price.Succeeded || string.IsNullOrWhiteSpace(price.StripePriceId)) return (false, price.Message);
        plan.StripePriceId = price.StripePriceId; await repository.AddPlanAsync(plan, token); await notifier.NotifyPlanCreatedAsync(token);
        return (true, $"Created plan {plan.PlanName}.");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(Guid id, SubscriptionPlanCommand command, CancellationToken token = default)
    {
        var validation = Validate(command); if (validation is not null) return (false, validation);
        var plan = await repository.FindPlanAsync(id, token); if (plan is null) return (false, "The selected plan no longer exists.");
        var code = NormalizeCode(command.PlanCode); if (await repository.PlanCodeExistsAsync(code, id, token)) return (false, "Another plan is already using that plan code.");
        plan.PlanCode = code; plan.PlanName = command.PlanName.Trim(); plan.Description = Optional(command.Description); plan.MonthlyPrice = command.MonthlyPrice; plan.MonthlyTokenLimit = command.MonthlyTokenLimit;
        plan.DailyTokenLimit = null; plan.WeeklyTokenLimit = null; plan.HasUnlimitedChat = false; plan.IsActive = command.IsActive;
        var price = await stripe.EnsurePlanPriceAsync(plan.PlanId, plan.PlanCode, plan.PlanName, plan.Description, plan.MonthlyPrice, plan.StripePriceId, token);
        if (!price.Succeeded || string.IsNullOrWhiteSpace(price.StripePriceId)) return (false, price.Message);
        plan.StripePriceId = price.StripePriceId; await repository.SavePlanAsync(plan, token); await notifier.NotifyPlanUpdatedAsync(token); return (true, $"Updated plan {plan.PlanName}.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(Guid id, CancellationToken token = default)
    {
        var plan = await repository.FindPlanAsync(id, token); if (plan is null) return (false, "The selected plan no longer exists.");
        if (!await repository.DeletePlanAsync(plan, token)) return (false, $"Cannot delete {plan.PlanName} because it has existing subscription or token usage records. You can hide it by turning off Active instead.");
        await notifier.NotifyPlanDeletedAsync(token); return (true, $"Deleted plan {plan.PlanName}.");
    }
    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? "";
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Validate(SubscriptionPlanCommand command) =>
        string.IsNullOrWhiteSpace(command.PlanCode) ? "Plan code is required." :
        string.IsNullOrWhiteSpace(command.PlanName) ? "Plan name is required." :
        command.MonthlyPrice < 0 ? "Monthly price cannot be negative." :
        command.MonthlyTokenLimit <= 0 ? "Monthly token limit must be greater than zero." : null;
}
