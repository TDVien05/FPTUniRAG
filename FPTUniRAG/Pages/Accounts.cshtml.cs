using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.BusinessLayer.Accounts.Importing;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public class AccountsModel : PageModel
{
    private readonly IAccountManagementService _accountManagementService;

    public AccountsModel(IAccountManagementService accountManagementService)
    {
        _accountManagementService = accountManagementService;
    }

    [BindProperty]
    public IFormFile? StudentSheet { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Teacher email is required.")]
    [EmailAddress(ErrorMessage = "Teacher email is invalid.")]
    public string TeacherEmail { get; set; } = string.Empty;

    public IReadOnlyList<ManagedAccountDto> Accounts { get; private set; } = [];

    public ImportStudentsResult? ImportResult { get; private set; }

    public string? SuccessMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAccountsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostImportStudentsAsync(CancellationToken cancellationToken)
    {
        if (StudentSheet is null || StudentSheet.Length == 0)
        {
            ErrorMessage = "Please choose a student import file first.";
            await LoadAccountsAsync(cancellationToken);
            return Page();
        }

        try
        {
            await using var stream = StudentSheet.OpenReadStream();
            ImportResult = await _accountManagementService.ImportStudentsAsync(
                stream,
                StudentSheet.FileName,
                cancellationToken);

            SuccessMessage = ImportResult.CreatedCount > 0
                ? $"Imported {ImportResult.CreatedCount} student account(s)."
                : "No student accounts were created.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }

        await LoadAccountsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateTeacherAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAccountsAsync(cancellationToken);
            return Page();
        }

        var result = await _accountManagementService.CreateTeacherAsync(TeacherEmail, cancellationToken);
        if (result.Succeeded)
        {
            SuccessMessage = result.Message;
            TeacherEmail = string.Empty;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        await LoadAccountsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSetAccountStatusAsync(Guid userId, bool isBlocked, CancellationToken cancellationToken)
    {
        var result = await _accountManagementService.SetAccountBlockedStatusAsync(userId, isBlocked, cancellationToken);
        if (result.Succeeded)
        {
            SuccessMessage = result.Message;
        }
        else
        {
            ErrorMessage = result.Message;
        }

        await LoadAccountsAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAccountsAsync(CancellationToken cancellationToken)
    {
        Accounts = await _accountManagementService.GetManagedAccountsAsync(cancellationToken);
    }
}
