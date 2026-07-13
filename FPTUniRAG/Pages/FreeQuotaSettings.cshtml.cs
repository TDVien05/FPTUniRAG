using System.Security.Claims;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class FreeQuotaSettingsModel : PageModel
{
    private readonly IFreeTokenQuotaService _freeTokenQuotaService;
    private readonly ISubscriptionPlanNotifier _subscriptionPlanNotifier;
    private readonly ILogger<FreeQuotaSettingsModel> _logger;

    public FreeQuotaSettingsModel(
        IFreeTokenQuotaService freeTokenQuotaService,
        ISubscriptionPlanNotifier subscriptionPlanNotifier,
        ILogger<FreeQuotaSettingsModel> logger)
    {
        _freeTokenQuotaService = freeTokenQuotaService;
        _subscriptionPlanNotifier = subscriptionPlanNotifier;
        _logger = logger;
    }

    [BindProperty]
    public long MonthlyTokenLimit { get; set; }

    public long CurrentMonthlyTokenLimit { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentMonthlyTokenLimit = await _freeTokenQuotaService.GetMonthlyTokenLimitAsync(cancellationToken);
        MonthlyTokenLimit = CurrentMonthlyTokenLimit;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (MonthlyTokenLimit <= 0)
        {
            ModelState.AddModelError(nameof(MonthlyTokenLimit), "Free monthly token limit must be greater than zero.");
        }

        if (!ModelState.IsValid)
        {
            CurrentMonthlyTokenLimit = await _freeTokenQuotaService.GetMonthlyTokenLimitAsync(cancellationToken);
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId))
        {
            ErrorMessage = "Your admin session is invalid. Please sign in again.";
            return RedirectToPage();
        }

        try
        {
            var savedLimit = await _freeTokenQuotaService.UpdateMonthlyTokenLimitAsync(
                MonthlyTokenLimit,
                adminUserId,
                cancellationToken);
            await _subscriptionPlanNotifier.NotifyFreeTokenLimitUpdatedAsync(cancellationToken);
            SuccessMessage = $"Free student token limit updated to {savedLimit:N0} tokens per month.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to update free student token limit.");
            ErrorMessage = "Unable to save the free token limit. Apply the free quota SQL script and try again.";
        }

        return RedirectToPage();
    }
}
