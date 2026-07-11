using System.Security.Claims;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.Pages;

public class StudentPlansModel : PageModel
{
    private const long DefaultFreeStudentMonthlyTokenLimit = 2000;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<StudentPlansModel> _logger;
    private readonly IStripePaymentService _stripePaymentService;

    public StudentPlansModel(
        AppDbContext dbContext,
        ILogger<StudentPlansModel> logger,
        IStripePaymentService stripePaymentService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _stripePaymentService = stripePaymentService;
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
            var paymentResult = await _stripePaymentService.CreateSubscriptionCheckoutAsync(userId, normalizedPlanCode, StudentName, StudentEmail, cancellationToken);
            if (!paymentResult.Succeeded || string.IsNullOrWhiteSpace(paymentResult.CheckoutUrl))
            {
                ErrorMessage = paymentResult.Message;
                return RedirectToPage();
            }

            return Redirect(paymentResult.CheckoutUrl);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to purchase student plan {PlanCode} for user {UserId}.", planCode, userId);
            ErrorMessage = "Unable to initialize Stripe checkout right now. Please try again.";
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
            .Select(plan => new StudentPlanSnapshot(
                plan.PlanId,
                plan.PlanCode,
                plan.PlanName,
                plan.Description,
                plan.MonthlyPrice,
                plan.MonthlyTokenLimit,
                plan.HasAdvancedModels,
                plan.HasPrioritySupport,
                plan.HasFileUpload,
                plan.HasHistoryExport))
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
                true,
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

    private static string[] BuildHighlights(StudentPlanSnapshot plan)
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
        bool IsPurchasable,
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

    private sealed record StudentPlanSnapshot(
        Guid PlanId,
        string PlanCode,
        string PlanName,
        string? Description,
        decimal MonthlyPrice,
        long? MonthlyTokenLimit,
        bool HasAdvancedModels,
        bool HasPrioritySupport,
        bool HasFileUpload,
        bool HasHistoryExport);
}
