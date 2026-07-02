using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

public class LoginModel : PageModel
{
    private const string DefaultAdminEmail = "admin@fpt.edu.vn";
    private const string DefaultAdminPassword = "Admin@123";
    private const string DefaultTeacherEmail = "teacher@fpt.edu.vn";
    private const string DefaultTeacherPassword = "Teacher@123";
    private const string DefaultStudentEmail = "student@fpt.edu.vn";
    private const string DefaultStudentPassword = "Student@123";
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
        var teacherEmail = _configuration["TeacherCredentials:Email"];
        var teacherPassword = _configuration["TeacherCredentials:Password"];
        var studentEmail = _configuration["StudentCredentials:Email"];
        var studentPassword = _configuration["StudentCredentials:Password"];

        adminEmail = string.IsNullOrWhiteSpace(adminEmail) ? DefaultAdminEmail : adminEmail.Trim();
        adminPassword = string.IsNullOrWhiteSpace(adminPassword) ? DefaultAdminPassword : adminPassword;
        teacherEmail = string.IsNullOrWhiteSpace(teacherEmail) ? DefaultTeacherEmail : teacherEmail.Trim();
        teacherPassword = string.IsNullOrWhiteSpace(teacherPassword) ? DefaultTeacherPassword : teacherPassword;
        studentEmail = string.IsNullOrWhiteSpace(studentEmail) ? DefaultStudentEmail : studentEmail.Trim();
        studentPassword = string.IsNullOrWhiteSpace(studentPassword) ? DefaultStudentPassword : studentPassword;

        if (string.Equals(Input.Email.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase)
            && Input.Password == adminPassword)
        {
            return RedirectToPage("/AdminDashboard");
        }

        if (string.Equals(Input.Email.Trim(), teacherEmail, StringComparison.OrdinalIgnoreCase)
            && Input.Password == teacherPassword)
        {
            return RedirectToPage("/TeacherHome");
        }

        if (string.Equals(Input.Email.Trim(), studentEmail, StringComparison.OrdinalIgnoreCase)
            && Input.Password == studentPassword)
        {
            return RedirectToPage("/StudentDashboard");
        }

        ModelState.AddModelError(string.Empty, "Invalid admin, teacher, or student email or password.");
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
