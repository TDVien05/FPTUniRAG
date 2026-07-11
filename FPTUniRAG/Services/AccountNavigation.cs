using System.Security.Claims;

namespace FPTUniRAG.Services;

public static class AccountNavigation
{
    public const string LoginPath = "/Login";
    public const string LogoutPath = "/Logout";
    public const string AdminLandingPath = "/AdminDashboard";
    public const string TeacherLandingPath = "/TeacherHome";
    public const string StudentLandingPath = "/StudentDashboard";

    public static string GetLandingPagePath(string? role)
    {
        return NormalizeRole(role) switch
        {
            "admin" => AdminLandingPath,
            "teacher" => TeacherLandingPath,
            _ => StudentLandingPath
        };
    }

    public static string GetLandingPagePath(ClaimsPrincipal? principal)
    {
        return GetLandingPagePath(principal?.FindFirstValue(ClaimTypes.Role));
    }

    public static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role)
            ? "student"
            : role.Trim().ToLowerInvariant();
    }
}
