using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[AllowAnonymous]
public class StripeReturnModel : PageModel
{
    private readonly IStripePaymentService _stripePaymentService;

    public StripeReturnModel(IStripePaymentService stripePaymentService)
    {
        _stripePaymentService = stripePaymentService;
    }

    public string Title { get; private set; } = "Payment status";

    public string Message { get; private set; } = "Processing payment result...";

    public bool IsSuccess { get; private set; }

    public string BackLink => User.Identity?.IsAuthenticated == true ? "/StudentPlans" : "/";

    public async Task OnGetAsync([FromQuery(Name = "session_id")] string? sessionId, CancellationToken cancellationToken)
    {
        var result = await _stripePaymentService.ConfirmCheckoutAsync(sessionId ?? string.Empty, cancellationToken);
        Title = result.Title;
        Message = result.Message;
        IsSuccess = result.IsPaid;
    }
}
