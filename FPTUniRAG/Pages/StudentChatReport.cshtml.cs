using FPTUniRAG.BusinessLayer.AdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class StudentChatReportModel(IStudentChatReportService reportService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SubjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public Guid? SessionId { get; set; }

    public StudentChatReportDto Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await reportService.GetReportAsync(
            new StudentChatReportQuery(Search, SubjectId, FromDate, ToDate, PageNumber, SessionId),
            cancellationToken);

        Search = Report.Search;
        SubjectId = Report.SubjectId;
        FromDate = Report.FromDate;
        ToDate = Report.ToDate;
        PageNumber = Report.Page;
        SessionId = Report.SelectedSession?.SessionId;
    }
}
