using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

public class LoginModel : PageModel
{
    private const string DefaultAdminEmail = "admin@fpt.edu.vn";
    private const string DefaultAdminPassword = "Admin@123";
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var adminEmail = _configuration["AdminCredentials:Email"];
        var adminPassword = _configuration["AdminCredentials:Password"];

        adminEmail = string.IsNullOrWhiteSpace(adminEmail) ? DefaultAdminEmail : adminEmail.Trim();
        adminPassword = string.IsNullOrWhiteSpace(adminPassword) ? DefaultAdminPassword : adminPassword;

        if (string.Equals(Input.Email.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase)
            && Input.Password == adminPassword)
        {
            return RedirectToPage("/AdminDashboard");
        }

        ModelState.AddModelError(string.Empty, "Invalid admin email or password.");
        return Page();
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
