using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FPTUniRAG.Services;

namespace FPTUniRAG.Pages;

[AllowAnonymous]
public class MomoReturnModel : PageModel
{
    private readonly IMomoPaymentService _momoPaymentService;

    public MomoReturnModel(IMomoPaymentService momoPaymentService)
    {
        _momoPaymentService = momoPaymentService;
    }

    public string Title { get; private set; } = "Payment status";

    public string Message { get; private set; } = "Processing payment result...";

    public bool IsSuccess { get; private set; }

    public string BackLink => User.Identity?.IsAuthenticated == true ? "/StudentPlans" : "/";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var parameters = Request.Query
            .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await _momoPaymentService.ProcessReturnAsync(parameters, cancellationToken);
        Title = result.Title;
        Message = result.Message;
        IsSuccess = result.IsPaid;
    }
}
