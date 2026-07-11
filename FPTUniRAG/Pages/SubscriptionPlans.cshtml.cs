using System.ComponentModel.DataAnnotations;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public class SubscriptionPlansModel : PageModel
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SubscriptionPlansModel> _logger;
    private readonly IStripePaymentService _stripePaymentService;

    public SubscriptionPlansModel(
        AppDbContext dbContext,
        ILogger<SubscriptionPlansModel> logger,
        IStripePaymentService stripePaymentService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _stripePaymentService = stripePaymentService;
    }

    [BindProperty]
    public CreatePlanInputModel CreateInput { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<SubscriptionPlanViewModel> Plans { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LoadPlansAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load subscription plans for admin.");
            ErrorMessage = "Unable to load subscription plans right now. Please verify the latest database schema has been applied.";
            Plans = [];
        }
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        var normalizedCode = NormalizePlanCode(CreateInput.PlanCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            ModelState.AddModelError("CreateInput.PlanCode", "Plan code is required.");
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        if (await _dbContext.SubscriptionPlans.AnyAsync(plan => plan.PlanCode == normalizedCode, cancellationToken))
        {
            ModelState.AddModelError("CreateInput.PlanCode", "That plan code already exists.");
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        var planId = Guid.NewGuid();

        var plan = new SubscriptionPlan
        {
            PlanId = planId,
            PlanCode = normalizedCode,
            PlanName = CreateInput.PlanName.Trim(),
            Description = NormalizeOptionalText(CreateInput.Description),
            StripePriceId = null,
            MonthlyPrice = CreateInput.MonthlyPrice,
            MonthlyTokenLimit = CreateInput.MonthlyTokenLimit,
            DailyTokenLimit = null,
            WeeklyTokenLimit = null,
            HasUnlimitedChat = false,
            HasAdvancedModels = CreateInput.HasAdvancedModels,
            HasPrioritySupport = CreateInput.HasPrioritySupport,
            HasFileUpload = CreateInput.HasFileUpload,
            HasHistoryExport = CreateInput.HasHistoryExport,
            IsActive = CreateInput.IsActive
        };

        var priceResult = await _stripePaymentService.EnsurePlanPriceAsync(
            plan.PlanId,
            plan.PlanCode,
            plan.PlanName,
            plan.Description,
            plan.MonthlyPrice,
            null,
            cancellationToken);
        if (!priceResult.Succeeded || string.IsNullOrWhiteSpace(priceResult.StripePriceId))
        {
            ModelState.AddModelError(string.Empty, priceResult.Message);
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        plan.StripePriceId = priceResult.StripePriceId;

        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);
        SuccessMessage = $"Created plan {CreateInput.PlanName.Trim()}.";
        return RedirectToPage("/SubscriptionPlans");
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        Guid planId,
        string planCode,
        string planName,
        string? description,
        decimal monthlyPrice,
        long monthlyTokenLimit,
        bool isActive,
        bool hasAdvancedModels,
        bool hasPrioritySupport,
        bool hasFileUpload,
        bool hasHistoryExport,
        CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlans.SingleOrDefaultAsync(item => item.PlanId == planId, cancellationToken);
        if (plan is null)
        {
            ErrorMessage = "The selected plan no longer exists.";
            return RedirectToPage("/SubscriptionPlans");
        }

        var normalizedCode = NormalizePlanCode(planCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            ErrorMessage = "Plan code is required.";
            return RedirectToPage("/SubscriptionPlans");
        }

        if (string.IsNullOrWhiteSpace(planName))
        {
            ErrorMessage = "Plan name is required.";
            return RedirectToPage("/SubscriptionPlans");
        }

        if (monthlyPrice < 0)
        {
            ErrorMessage = "Monthly price cannot be negative.";
            return RedirectToPage("/SubscriptionPlans");
        }

        if (monthlyTokenLimit <= 0)
        {
            ErrorMessage = "Monthly token limit must be greater than zero.";
            return RedirectToPage("/SubscriptionPlans");
        }

        var duplicateCodeExists = await _dbContext.SubscriptionPlans
            .AnyAsync(item => item.PlanId != planId && item.PlanCode == normalizedCode, cancellationToken);
        if (duplicateCodeExists)
        {
            ErrorMessage = "Another plan is already using that plan code.";
            return RedirectToPage("/SubscriptionPlans");
        }

        plan.PlanCode = normalizedCode;
        plan.PlanName = planName.Trim();
        plan.Description = NormalizeOptionalText(description);
        plan.MonthlyPrice = monthlyPrice;
        plan.MonthlyTokenLimit = monthlyTokenLimit;
        plan.DailyTokenLimit = null;
        plan.WeeklyTokenLimit = null;
        plan.HasUnlimitedChat = false;
        plan.HasAdvancedModels = hasAdvancedModels;
        plan.HasPrioritySupport = hasPrioritySupport;
        plan.HasFileUpload = hasFileUpload;
        plan.HasHistoryExport = hasHistoryExport;
        plan.IsActive = isActive;

        var priceResult = await _stripePaymentService.EnsurePlanPriceAsync(
            plan.PlanId,
            plan.PlanCode,
            plan.PlanName,
            plan.Description,
            plan.MonthlyPrice,
            plan.StripePriceId,
            cancellationToken);
        if (!priceResult.Succeeded || string.IsNullOrWhiteSpace(priceResult.StripePriceId))
        {
            ErrorMessage = priceResult.Message;
            return RedirectToPage("/SubscriptionPlans");
        }

        plan.StripePriceId = priceResult.StripePriceId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        SuccessMessage = $"Updated plan {plan.PlanName}.";
        return RedirectToPage("/SubscriptionPlans");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlans.SingleOrDefaultAsync(item => item.PlanId == planId, cancellationToken);
        if (plan is null)
        {
            ErrorMessage = "The selected plan no longer exists.";
            return RedirectToPage("/SubscriptionPlans");
        }

        var hasStudentSubscriptions = await _dbContext.StudentSubscriptions
            .AnyAsync(subscription => subscription.PlanId == planId, cancellationToken);
        var hasTokenUsageLogs = await _dbContext.TokenUsageLogs
            .AnyAsync(log => log.PlanId == planId, cancellationToken);

        if (hasStudentSubscriptions || hasTokenUsageLogs)
        {
            ErrorMessage = $"Cannot delete {plan.PlanName} because it has existing subscription or token usage records. You can hide it by turning off Active instead.";
            return RedirectToPage("/SubscriptionPlans");
        }

        _dbContext.SubscriptionPlans.Remove(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);
        SuccessMessage = $"Deleted plan {plan.PlanName}.";
        return RedirectToPage("/SubscriptionPlans");
    }

    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        var planEntities = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(plan => plan.MonthlyPrice)
            .ThenBy(plan => plan.PlanName)
            .ToListAsync(cancellationToken);

        var subscriptionPlanIds = await _dbContext.StudentSubscriptions
            .AsNoTracking()
            .Select(subscription => subscription.PlanId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var tokenUsagePlanIds = await _dbContext.TokenUsageLogs
            .AsNoTracking()
            .Where(log => log.PlanId != null)
            .Select(log => log.PlanId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var subscriptionPlanIdSet = subscriptionPlanIds.ToHashSet();
        var tokenUsagePlanIdSet = tokenUsagePlanIds.ToHashSet();

        Plans = planEntities
            .Select(plan => new SubscriptionPlanViewModel(
                plan.PlanId,
                plan.PlanCode,
                plan.PlanName,
                plan.Description,
                plan.MonthlyPrice,
                plan.MonthlyTokenLimit ?? 0,
                plan.IsActive,
                plan.HasAdvancedModels,
                plan.HasPrioritySupport,
                plan.HasFileUpload,
                plan.HasHistoryExport,
                subscriptionPlanIdSet.Contains(plan.PlanId),
                tokenUsagePlanIdSet.Contains(plan.PlanId)))
            .ToList();
    }

    private static string NormalizePlanCode(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed record SubscriptionPlanViewModel(
        Guid PlanId,
        string PlanCode,
        string PlanName,
        string? Description,
        decimal MonthlyPrice,
        long MonthlyTokenLimit,
        bool IsActive,
        bool HasAdvancedModels,
        bool HasPrioritySupport,
        bool HasFileUpload,
        bool HasHistoryExport,
        bool HasStudentSubscriptions,
        bool HasTokenUsageLogs)
    {
        public bool CanDelete => !HasStudentSubscriptions && !HasTokenUsageLogs;
    }

    public sealed class CreatePlanInputModel
    {
        [Required(ErrorMessage = "Plan code is required.")]
        public string PlanCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Plan name is required.")]
        public string PlanName { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Monthly price must be zero or greater.")]
        public decimal MonthlyPrice { get; set; }

        [Range(1, long.MaxValue, ErrorMessage = "Monthly token limit must be greater than zero.")]
        public long MonthlyTokenLimit { get; set; }

        public bool IsActive { get; set; } = true;

        public bool HasAdvancedModels { get; set; }

        public bool HasPrioritySupport { get; set; }

        public bool HasFileUpload { get; set; } = true;

        public bool HasHistoryExport { get; set; }
    }
}
