using FPTUniRAG.BusinessLayer.Subjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages.Subjects;

[Authorize(Policy = "AdminOnly")]
public class AssignHeaderModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public AssignHeaderModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    [BindProperty]
    public Guid SubjectId { get; set; }

    [BindProperty]
    public Guid TeacherId { get; set; }

    public IReadOnlyList<SubjectHeaderAssignmentListItemDto> Subjects { get; private set; } = [];

    public IReadOnlyList<TeacherOptionDto> Teachers { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadDataAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (SubjectId == Guid.Empty)
        {
            ErrorMessage = "Please choose a subject.";
            await LoadDataAsync(cancellationToken);
            return Page();
        }

        if (TeacherId == Guid.Empty)
        {
            ErrorMessage = "Please choose a teacher.";
            await LoadDataAsync(cancellationToken);
            return Page();
        }

        var result = await _subjectManagementService.AssignHeaderTeacherAsync(
            SubjectId,
            TeacherId,
            cancellationToken);

        if (!result.Succeeded)
        {
            ErrorMessage = result.Message;
            await LoadDataAsync(cancellationToken);
            return Page();
        }

        SuccessMessage = result.Message;
        return RedirectToPage();
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        Subjects = await _subjectManagementService.GetSubjectsForHeaderAssignmentAsync(cancellationToken);
        Teachers = await _subjectManagementService.GetTeacherOptionsAsync(cancellationToken);
    }
}
