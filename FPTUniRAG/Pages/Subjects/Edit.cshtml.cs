using FPTUniRAG.BusinessLayer.Subjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages.Subjects;

[Authorize(Policy = "AdminOnly")]
public class EditModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public EditModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    [BindProperty]
    public SubjectInputModel Input { get; set; } = new();

    public Guid SubjectId { get; private set; }

    public DateTime? CreatedAt { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var subject = await _subjectManagementService.GetSubjectForEditAsync(subjectId, cancellationToken);
        if (subject is null)
        {
            ErrorMessage = "The selected subject no longer exists.";
            return RedirectToPage("/Subjects");
        }

        SubjectId = subject.SubjectId;
        CreatedAt = subject.CreatedAt;
        Input = new SubjectInputModel
        {
            SubjectCode = subject.SubjectCode,
            SubjectName = subject.SubjectName,
            Description = subject.Description,
            DefaultChunkingStrategy = subject.DefaultChunkingStrategy
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        SubjectId = subjectId;

        var subject = await _subjectManagementService.GetSubjectForEditAsync(subjectId, cancellationToken);
        if (subject is null)
        {
            ErrorMessage = "The selected subject no longer exists.";
            return RedirectToPage("/Subjects");
        }

        CreatedAt = subject.CreatedAt;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _subjectManagementService.UpdateSubjectAsync(
            subjectId,
            new UpsertSubjectRequest(
                Input.SubjectCode,
                Input.SubjectName,
                Input.Description,
                Input.DefaultChunkingStrategy),
            cancellationToken);

        if (!result.Succeeded)
        {
            ErrorMessage = result.Message;
            return Page();
        }

        SuccessMessage = result.Message;
        return RedirectToPage("/Subjects");
    }
}
