using FPTUniRAG.BusinessLayer.Rag.Ingestion;
using FPTUniRAG.BusinessLayer.Subjects;
using System.Security.Claims;
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

    [BindProperty(SupportsGet = true)]
    public Guid? SubjectId { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<TeacherDocumentManagementItemDto> ManagedSubjects { get; private set; } = [];

    public TeacherDocumentManagementItemDto? SelectedSubject { get; private set; }

    public IReadOnlyList<TeacherSubjectDocumentDto> VisibleDocuments { get; private set; } = [];

    public int CompletedDocumentCount => SelectedSubject?.Documents.Count(document =>
        string.Equals(document.Status, "completed", StringComparison.OrdinalIgnoreCase)) ?? 0;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            ManagedSubjects = [];
            return;
        }

        ManagedSubjects = await _subjectManagementService.GetDocumentManagementItemsForTeacherAsync(
            teacherEmail,
            cancellationToken);

        SelectedSubject = SubjectId.HasValue
            ? ManagedSubjects.FirstOrDefault(item => item.SubjectId == SubjectId.Value)
            : null;
        SelectedSubject ??= ManagedSubjects.FirstOrDefault();
        SubjectId = SelectedSubject?.SubjectId;

        if (SelectedSubject is null)
        {
            VisibleDocuments = [];
            return;
        }

        var documents = SelectedSubject.Documents;
        if (!string.IsNullOrWhiteSpace(Query))
        {
            var normalizedQuery = Query.Trim();
            documents = documents
                .Where(document =>
                    document.ChapterTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || document.DocumentTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        VisibleDocuments = documents;
    }

    public async Task<IActionResult> OnPostRetryAsync(Guid documentId, Guid subjectId, CancellationToken cancellationToken)
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

        if (!result.Succeeded)
        {
            ErrorMessage = result.Message;
            return RedirectToPage(new { subjectId, Query });
        }

        return RedirectToPage("/TeacherUpload", new { subjectId, processingDocumentId = documentId });
    }

    public async Task<IActionResult> OnPostDeleteChapterAsync(
        Guid subjectId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        var result = await _teacherDocumentWorkflowService.DeleteChapterAsync(
            teacherEmail,
            subjectId,
            chapterId,
            cancellationToken);

        if (result.Succeeded)
        {
            SuccessMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        return RedirectToPage(new { subjectId, Query });
    }
}
