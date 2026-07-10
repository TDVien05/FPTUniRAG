using System.Security.Claims;
using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.Pages;

public class StudentPlansModel : PageModel
{
    private const long DefaultFreeStudentMonthlyTokenLimit = 2000;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<StudentPlansModel> _logger;

    public StudentPlansModel(AppDbContext dbContext, ILogger<StudentPlansModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string StudentName => User.FindFirstValue(ClaimTypes.Name)?.Trim() ?? "Student";

    public string StudentEmail => User.FindFirstValue(ClaimTypes.Email)?.Trim() ?? string.Empty;

    public IReadOnlyList<StudentPlanCardViewModel> Plans { get; private set; } = [];

    public StudentCurrentPlanViewModel? CurrentPlan { get; private set; }

    public decimal TokensUsedThisMonth { get; private set; }

    public decimal? TokensRemainingThisMonth =>
        CurrentPlan?.MonthlyTokenLimit is > 0
            ? Math.Max(0m, CurrentPlan.MonthlyTokenLimit.Value - TokensUsedThisMonth)
            : null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(GetRequiredUserId(), cancellationToken);
    }

    public async Task<IActionResult> OnPostPurchaseAsync(string planCode, CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();

        if (string.IsNullOrWhiteSpace(planCode))
        {
            ErrorMessage = "Please choose a plan before continuing.";
            return RedirectToPage();
        }

        var normalizedPlanCode = planCode.Trim().ToLowerInvariant();

        try
        {
            var selectedPlan = await _dbContext.SubscriptionPlans
                .SingleOrDefaultAsync(
                    plan => plan.IsActive && plan.PlanCode == normalizedPlanCode,
                    cancellationToken);

            if (selectedPlan is null)
            {
                ErrorMessage = "The selected plan is not available right now.";
                return RedirectToPage();
            }

            var now = DateTime.UtcNow;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var activeSubscriptions = await _dbContext.StudentSubscriptions
                .Where(subscription => subscription.UserId == userId && subscription.SubscriptionStatus == "active")
                .ToListAsync(cancellationToken);

            if (activeSubscriptions.Any(subscription => subscription.PlanId == selectedPlan.PlanId))
            {
                SuccessMessage = $"Your {selectedPlan.PlanName} plan is already active.";
                await transaction.RollbackAsync(cancellationToken);
                return RedirectToPage();
            }

            foreach (var activeSubscription in activeSubscriptions)
            {
                activeSubscription.SubscriptionStatus = "replaced";
                activeSubscription.CanceledAt = now;
                activeSubscription.ExpiresAt ??= now;
                activeSubscription.Notes = $"Replaced by {selectedPlan.PlanCode} via student purchase page.";
            }

            _dbContext.StudentSubscriptions.Add(new()
            {
                UserId = userId,
                PlanId = selectedPlan.PlanId,
                SubscriptionStatus = "active",
                StartedAt = now,
                PurchasedAt = now,
                ExpiresAt = now.AddMonths(1),
                AutoRenew = false,
                Notes = $"Purchased by student via StudentPlans on {now:yyyy-MM-dd HH:mm:ss} UTC."
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            SuccessMessage = $"Switched to {selectedPlan.PlanName}. Your token allocation is now {selectedPlan.MonthlyTokenLimit:N0} per month.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to purchase student plan {PlanCode} for user {UserId}.", planCode, userId);
            ErrorMessage = "Unable to activate that plan right now. Please try again.";
        }

        return RedirectToPage();
    }

    private async Task LoadPageStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activePlans = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.MonthlyPrice)
            .ThenBy(plan => plan.PlanName)
            .ToListAsync(cancellationToken);

        var entitlement = await _dbContext.StudentActiveChatEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        var usage = await _dbContext.StudentTokenUsageCurrentMonths
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        TokensUsedThisMonth = usage?.TotalTokensUsedThisMonth ?? 0;

        var recommendedPlanId = activePlans.Count >= 2
            ? activePlans[1].PlanId
            : activePlans.FirstOrDefault()?.PlanId;

        Plans = activePlans
            .Select(plan => new StudentPlanCardViewModel(
                plan.PlanCode,
                plan.PlanName,
                plan.Description ?? "Configured by the admin team for your current study workload.",
                plan.MonthlyPrice,
                plan.MonthlyTokenLimit ?? 0,
                plan.HasAdvancedModels,
                plan.HasPrioritySupport,
                plan.HasFileUpload,
                plan.HasHistoryExport,
                recommendedPlanId == plan.PlanId,
                string.Equals(entitlement?.PlanCode, plan.PlanCode, StringComparison.OrdinalIgnoreCase),
                BuildHighlights(plan)))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(entitlement?.PlanCode))
        {
            CurrentPlan = new StudentCurrentPlanViewModel(
                entitlement.PlanCode!,
                entitlement.PlanName ?? "Active plan",
                entitlement.MonthlyTokenLimit,
                entitlement.HasAdvancedModels ?? false,
                entitlement.HasPrioritySupport ?? false,
                entitlement.HasHistoryExport ?? false,
                entitlement.ExpiresAt);
            return;
        }

        CurrentPlan = new StudentCurrentPlanViewModel(
            "free",
            "Free",
            DefaultFreeStudentMonthlyTokenLimit,
            false,
            false,
            false,
            null);
    }

    private static string[] BuildHighlights(DataAccessLayer.Entities.SubscriptionPlan plan)
    {
        var highlights = new List<string>();

        if (plan.MonthlyTokenLimit is > 0)
        {
            highlights.Add($"{plan.MonthlyTokenLimit.Value:N0} tokens per month");
        }

        highlights.Add(plan.HasAdvancedModels ? "Advanced reasoning models" : "Standard study model");
        highlights.Add(plan.HasHistoryExport ? "History and citation export" : "In-app citation browsing");
        highlights.Add(plan.HasPrioritySupport ? "Priority support for study issues" : "Standard support response");
        highlights.Add(plan.HasFileUpload ? "File-based study context" : "Chat-only usage");

        return highlights.ToArray();
    }

    private Guid GetRequiredUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user identifier is missing.");
    }

    public sealed record StudentPlanCardViewModel(
        string PlanCode,
        string PlanName,
        string Description,
        decimal MonthlyPrice,
        long MonthlyTokenLimit,
        bool HasAdvancedModels,
        bool HasPrioritySupport,
        bool HasFileUpload,
        bool HasHistoryExport,
        bool IsRecommended,
        bool IsCurrent,
        IReadOnlyList<string> Highlights);

    public sealed record StudentCurrentPlanViewModel(
        string PlanCode,
        string PlanName,
        long? MonthlyTokenLimit,
        bool HasAdvancedModels,
        bool HasPrioritySupport,
        bool HasHistoryExport,
        DateTime? ExpiresAt);
}
