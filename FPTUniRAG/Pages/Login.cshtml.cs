using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class LoginModel : PageModel
{
    private readonly IAccountManagementService _accountManagementService;

    public LoginModel(IAccountManagementService accountManagementService)
    {
        _accountManagementService = accountManagementService;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? StatusMessage { get; private set; }

    public IActionResult OnGet()
    {
        ApplyNoStoreHeaders();

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage(AccountNavigation.GetLandingPagePath(User.FindFirstValue(ClaimTypes.Role)));
        }

        if (TempData.TryGetValue("LoginStatusMessage", out var loginStatusMessage))
        {
            StatusMessage = loginStatusMessage?.ToString();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ApplyNoStoreHeaders();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var authenticationResult = await _accountManagementService.AuthenticateAsync(Input.Email, Input.Password);
        if (authenticationResult.Status == AuthenticationStatus.Blocked)
        {
            ModelState.AddModelError(string.Empty, "This account has been blocked by an administrator.");
            return Page();
        }

        if (authenticationResult.Status != AuthenticationStatus.Success || authenticationResult.User is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }

        var user = authenticationResult.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));

        var authenticationProperties = new AuthenticationProperties
        {
            IsPersistent = Input.RememberMe,
            ExpiresUtc = Input.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(1)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authenticationProperties);

        return RedirectToPage(AccountNavigation.GetLandingPagePath(user.Role));
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    public class LoginInput
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid FPT email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
