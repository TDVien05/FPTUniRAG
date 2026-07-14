using FPTUniRAG.BusinessLayer.Subjects;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "TeacherOrAdmin")]
public class TeacherHomeModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public TeacherHomeModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    public IReadOnlyList<TeacherHeaderSubjectDashboardItemDto> HeaderSubjects { get; private set; } = [];

    public int TotalUploadedDocuments => HeaderSubjects.Sum(subject => subject.DocumentCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            HeaderSubjects = [];
            return;
        }

        HeaderSubjects = await _subjectManagementService.GetHeaderSubjectsForTeacherAsync(
            teacherEmail,
            cancellationToken);
    }
}
