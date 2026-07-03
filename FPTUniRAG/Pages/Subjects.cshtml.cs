using FPTUniRAG.BusinessLayer.Subjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public class SubjectsModel : PageModel
{
    private readonly ISubjectManagementService _subjectManagementService;

    public SubjectsModel(ISubjectManagementService subjectManagementService)
    {
        _subjectManagementService = subjectManagementService;
    }

    public IReadOnlyList<SubjectListItemDto> Subjects { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Subjects = await _subjectManagementService.GetSubjectsAsync(cancellationToken);
    }
}
