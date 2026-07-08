using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "TeacherOrAdmin")]
public class TeacherDocumentsModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;
    private readonly ITeacherDocumentWorkflowService _teacherDocumentWorkflowService;

    public TeacherDocumentsModel(
        ISubjectManagementService subjectManagementService,
        ITeacherDocumentWorkflowService teacherDocumentWorkflowService)
    {
        _subjectManagementService = subjectManagementService;
        _teacherDocumentWorkflowService = teacherDocumentWorkflowService;
    }

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<TeacherDocumentManagementItemDto> DocumentsBySubject { get; private set; } = [];

    public int ManagedSubjectCount => DocumentsBySubject.Count;

    public int TotalDocumentCount => DocumentsBySubject.Sum(item => item.DocumentCount);

    public int SubjectsWithDocumentsCount => DocumentsBySubject.Count(item => item.DocumentCount > 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            DocumentsBySubject = [];
            return;
        }

        var items = await _subjectManagementService.GetDocumentManagementItemsForTeacherAsync(
            teacherEmail,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var normalizedQuery = Query.Trim();
            items = items
                .Where(item =>
                    item.SubjectCode.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || item.SubjectName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(item.LatestDocumentTitle)
                        && item.LatestDocumentTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        DocumentsBySubject = items;
    }

    public async Task<IActionResult> OnPostRetryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        var result = await _teacherDocumentWorkflowService.RetryEmbeddingSyncAsync(
            teacherEmail,
            documentId,
            cancellationToken);

        if (result.Succeeded)
        {
            SuccessMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { Query });
    }
}
