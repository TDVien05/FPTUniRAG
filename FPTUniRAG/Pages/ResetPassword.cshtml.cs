using FPTUniRAG.BusinessLayer.Accounts;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ResetPasswordModel : PageModel
{
    private readonly IAccountManagementService _accountManagementService;

    public ResetPasswordModel(IAccountManagementService accountManagementService)
    {
        _accountManagementService = accountManagementService;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsTokenMissing { get; private set; }

    public bool ResetSucceeded { get; private set; }

    public string? SuccessMessage { get; private set; }

    public void OnGet()
    {
        IsTokenMissing = string.IsNullOrWhiteSpace(Token);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            IsTokenMissing = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _accountManagementService.ResetPasswordAsync(Token, Input.NewPassword, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        ResetSucceeded = true;
        SuccessMessage = result.Message;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "New password is required.")]
        [DataType(DataType.Password)]
        [MinLength(8, ErrorMessage = "New password must be at least 8 characters long.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm password is required.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Password confirmation does not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
