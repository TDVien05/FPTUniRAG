using System.ComponentModel.DataAnnotations;
using FPTUniRAG.BusinessLayer.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public class SubscriptionPlansModel(ISubscriptionPlanManagementService service, ILogger<SubscriptionPlansModel> logger) : PageModel
{
    [BindProperty] public CreatePlanInputModel CreateInput { get; set; } = new();
    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }
    public IReadOnlyList<SubscriptionPlanViewModel> Plans { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken token) { try { await LoadPlansAsync(token); } catch (Exception ex) { logger.LogError(ex, "Failed to load subscription plans for admin."); ErrorMessage = "Unable to load subscription plans right now. Please verify the latest database schema has been applied."; } }
    public async Task<IActionResult> OnPostCreateAsync(CancellationToken token)
    {
        if (!ModelState.IsValid) { await LoadPlansAsync(token); return Page(); }
        var result = await service.CreateAsync(ToCommand(CreateInput), token);
        if (!result.Success) { ModelState.AddModelError(string.Empty, result.Message); await LoadPlansAsync(token); return Page(); }
        SuccessMessage = result.Message; return RedirectToPage("/SubscriptionPlans");
    }
    public async Task<IActionResult> OnPostUpdateAsync(Guid planId, string planCode, string planName, string? description, decimal monthlyPrice, long monthlyTokenLimit, bool isActive, CancellationToken token)
    {
        var result = await service.UpdateAsync(planId, new(planCode, planName, description, monthlyPrice, monthlyTokenLimit, isActive), token);
        if (result.Success) SuccessMessage = result.Message; else ErrorMessage = result.Message;
        return RedirectToPage("/SubscriptionPlans");
    }
    public async Task<IActionResult> OnPostDeleteAsync(Guid planId, CancellationToken token)
    {
        var result = await service.DeleteAsync(planId, token); if (result.Success) SuccessMessage = result.Message; else ErrorMessage = result.Message;
        return RedirectToPage("/SubscriptionPlans");
    }
    private async Task LoadPlansAsync(CancellationToken token) => Plans = (await service.GetPlansAsync(token)).Select(p => new SubscriptionPlanViewModel(p.PlanId, p.PlanCode, p.PlanName, p.Description, p.MonthlyPrice, p.MonthlyTokenLimit, p.IsActive, p.HasAdvancedModels, p.HasPrioritySupport, p.HasFileUpload, p.HasHistoryExport, p.HasStudentSubscriptions, p.HasTokenUsageLogs)).ToList();
    private static SubscriptionPlanCommand ToCommand(CreatePlanInputModel x) => new(x.PlanCode, x.PlanName, x.Description, x.MonthlyPrice, x.MonthlyTokenLimit, x.IsActive);

    public sealed record SubscriptionPlanViewModel(Guid PlanId, string PlanCode, string PlanName, string? Description, decimal MonthlyPrice, long MonthlyTokenLimit, bool IsActive, bool HasAdvancedModels, bool HasPrioritySupport, bool HasFileUpload, bool HasHistoryExport, bool HasStudentSubscriptions, bool HasTokenUsageLogs) { public bool CanDelete => !HasStudentSubscriptions && !HasTokenUsageLogs; }
    public sealed class CreatePlanInputModel
    {
        [Required(ErrorMessage = "Plan code is required.")] public string PlanCode { get; set; } = "";
        [Required(ErrorMessage = "Plan name is required.")] public string PlanName { get; set; } = "";
        public string? Description { get; set; }
        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Monthly price must be zero or greater.")] public decimal MonthlyPrice { get; set; }
        [Range(1, long.MaxValue, ErrorMessage = "Monthly token limit must be greater than zero.")] public long MonthlyTokenLimit { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
