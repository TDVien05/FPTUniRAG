using FPTUniRAG.BusinessLayer.AdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminDashboardModel : PageModel
{
    private readonly IAdminDashboardService _adminDashboardService;

    public AdminDashboardModel(IAdminDashboardService adminDashboardService)
    {
        _adminDashboardService = adminDashboardService;
    }

    public AdminDashboardDto Dashboard { get; private set; } = new(0, 0, []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Dashboard = await _adminDashboardService.GetDashboardAsync(cancellationToken);
    }

    public static string GetStatusLabel(AdminDashboardSubjectDto subject)
    {
        if (subject.DocumentCount == 0)
        {
            return "No documents";
        }

        var status = subject.LatestDocumentStatus?.Trim();
        return string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : char.ToUpperInvariant(status[0]) + status[1..].ToLowerInvariant();
    }

    public static string GetStatusCssClass(AdminDashboardSubjectDto subject)
    {
        return subject.LatestDocumentStatus?.Trim().ToLowerInvariant() switch
        {
            "completed" => "success",
            "queued" or "processing" => "pending",
            _ => "draft"
        };
    }

    public static string FormatLastUpdated(DateTime? value)
    {
        return value?.ToString("dd/MM/yyyy HH:mm") ?? "Not available";
    }
}
