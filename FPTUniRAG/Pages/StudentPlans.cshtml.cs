using FPTUniRAG.BusinessLayer.Payments.Stripe;
using FPTUniRAG.BusinessLayer.Subscriptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

public class StudentPlansModel : PageModel
{
    private readonly IStudentPlanService _studentPlanService;
    private readonly ILogger<StudentPlansModel> _logger;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IFreeTokenQuotaService _freeTokenQuotaService;

    public StudentPlansModel(
        IStudentPlanService studentPlanService,
        ILogger<StudentPlansModel> logger,
        IStripePaymentService stripePaymentService,
        IFreeTokenQuotaService freeTokenQuotaService)
    {
        _studentPlanService = studentPlanService;
        _logger = logger;
        _stripePaymentService = stripePaymentService;
        _freeTokenQuotaService = freeTokenQuotaService;
    }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string StudentName => User.FindFirstValue(ClaimTypes.Name)?.Trim() ?? "Student";

    public string StudentEmail => User.FindFirstValue(ClaimTypes.Email)?.Trim() ?? string.Empty;

    public IReadOnlyList<StudentPlanCardViewModel> Plans { get; private set; } = [];

    public StudentCurrentPlanViewModel? CurrentPlan { get; private set; }

    public bool HasActiveSubscription { get; private set; }

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
        if (!await _studentPlanService.CanPurchaseAsync(userId, cancellationToken))
        {
            ErrorMessage = "You already have an active subscription. You cannot purchase another plan right now.";
            return RedirectToPage();
        }

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
        var state = await _studentPlanService.GetStateAsync(userId, cancellationToken);
        var activePlans = state.Plans.Select(p => new StudentPlanSnapshot(p.PlanId, p.PlanCode, p.PlanName, p.Description, p.MonthlyPrice, p.MonthlyTokenLimit, p.HasAdvancedModels, p.HasPrioritySupport, p.HasFileUpload, p.HasHistoryExport)).ToList();
        HasActiveSubscription = state.HasActiveSubscription;
        TokensUsedThisMonth = state.TokensUsedThisMonth;
        var canReplaceActiveSubscription = state.CanReplace;

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
                !HasActiveSubscription || canReplaceActiveSubscription,
                recommendedPlanId == plan.PlanId,
                state.CurrentPlan.PlanId == plan.PlanId,
                BuildHighlights(plan)))
            .ToArray();

        if (state.HasActiveSubscription)
        {
            CurrentPlan = new StudentCurrentPlanViewModel(
                state.CurrentPlan.PlanCode, state.CurrentPlan.PlanName, state.CurrentPlan.MonthlyPrice,
                state.CurrentPlan.MonthlyTokenLimit, state.CurrentPlan.StartedAt, state.CurrentPlan.ExpiresAt);
            return;
        }

        CurrentPlan = new StudentCurrentPlanViewModel(
            "free",
            "Free",
            0,
            state.CurrentPlan.MonthlyTokenLimit,
            null,
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

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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
        decimal MonthlyPrice,
        long? MonthlyTokenLimit,
        DateTime? StartedAt,
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
