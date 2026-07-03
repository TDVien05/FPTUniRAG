using FPTUniRAG.BusinessLayer.Subjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages.Subjects;

[Authorize(Policy = "AdminOnly")]
public class CreateModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public CreateModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    [BindProperty]
    public SubjectInputModel Input { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _subjectManagementService.CreateSubjectAsync(
            new UpsertSubjectRequest(Input.SubjectCode, Input.SubjectName, Input.Description),
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
