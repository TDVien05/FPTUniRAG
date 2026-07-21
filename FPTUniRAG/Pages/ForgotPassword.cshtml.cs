using FPTUniRAG.BusinessLayer.Accounts;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ForgotPasswordModel : PageModel
{
    private readonly IAccountManagementService _accountManagementService;

    public ForgotPasswordModel(IAccountManagementService accountManagementService)
    {
        _accountManagementService = accountManagementService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }

    public bool RequestSubmitted { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var resetPageUrl = Url.Page("/ResetPassword", pageHandler: null, values: null, protocol: Request.Scheme)
            ?? throw new InvalidOperationException("Unable to resolve the reset password page URL.");

        var result = await _accountManagementService.RequestPasswordResetAsync(Input.Email, resetPageUrl, cancellationToken);

        StatusMessage = result.Message;
        RequestSubmitted = true;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;
    }
}
