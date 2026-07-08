using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize]
public class ChangePasswordModel : PageModel
{
    private readonly IAccountManagementService _accountManagementService;

    public ChangePasswordModel(IAccountManagementService accountManagementService)
    {
        _accountManagementService = accountManagementService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    public string? Role => User.FindFirstValue(ClaimTypes.Role)?.Trim().ToLowerInvariant();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "Your account session is invalid. Please sign in again.");
            return Page();
        }

        var result = await _accountManagementService.ChangePasswordAsync(
            email,
            Input.CurrentPassword,
            Input.NewPassword,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        SuccessMessage = result.Message;
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

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
