using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "TeacherOrAdmin")]
public class TeacherDocumentDetailsModel : PageModel
{
    private readonly ITeacherDocumentWorkflowService _teacherDocumentWorkflowService;

    public TeacherDocumentDetailsModel(ITeacherDocumentWorkflowService teacherDocumentWorkflowService)
    {
        _teacherDocumentWorkflowService = teacherDocumentWorkflowService;
    }

    [TempData]
    public string? NoticeMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public TeacherDocumentDetailDto? Document { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        Document = await _teacherDocumentWorkflowService.GetDocumentDetailAsync(
            teacherEmail,
            documentId,
            cancellationToken);

        if (Document is null)
        {
            return Forbid();
        }

        return Page();
    }
}
