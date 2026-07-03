using FPTUniRAG.BusinessLayer.Subjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages.Subjects;

[Authorize(Policy = "AdminOnly")]
public class DeleteModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public DeleteModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    public SubjectDeletePreviewDto? Subject { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Subject = await _subjectManagementService.GetSubjectDeletePreviewAsync(subjectId, cancellationToken);
        if (Subject is null)
        {
            ErrorMessage = "The selected subject no longer exists.";
            return RedirectToPage("/Subjects");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var result = await _subjectManagementService.DeleteSubjectAsync(subjectId, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.Message;
            return RedirectToPage("/Subjects");
        }

        SuccessMessage = result.Message;
        return RedirectToPage("/Subjects");
    }
}
