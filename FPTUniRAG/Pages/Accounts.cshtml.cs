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
    private readonly IStudentImportJobTracker _importJobTracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public AccountsModel(
        IAccountManagementService accountManagementService,
        IStudentImportJobTracker importJobTracker,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime)
    {
        _accountManagementService = accountManagementService;
        _importJobTracker = importJobTracker;
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
    }

    [BindProperty]
    public IFormFile? StudentSheet { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Teacher email is required.")]
    [EmailAddress(ErrorMessage = "Teacher email is invalid.")]
    public string TeacherEmail { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public Guid? ImportJobId { get; set; }

    public IReadOnlyList<ManagedAccountDto> Accounts { get; private set; } = [];

    public ImportStudentsResult? ImportResult { get; private set; }

    public string? SuccessMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (ImportJobId.HasValue)
        {
            var status = _importJobTracker.GetStatus(ImportJobId.Value);
            if (status?.Result is not null)
            {
                ImportResult = status.Result;
                SuccessMessage = ImportResult.CreatedCount > 0
                    ? $"Imported {ImportResult.CreatedCount} student account(s)."
                    : "No student accounts were created.";
            }
            else if (status?.IsFailed == true)
            {
                ErrorMessage = status.ErrorMessage;
            }
        }

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

        byte[] fileBytes;
        await using (var uploadStream = StudentSheet.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await uploadStream.CopyToAsync(buffer, cancellationToken);
            fileBytes = buffer.ToArray();
        }

        var fileName = StudentSheet.FileName;
        var jobId = _importJobTracker.CreateJob();
        ImportJobId = jobId;

        var stoppingToken = _applicationLifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var accountManagementService = scope.ServiceProvider.GetRequiredService<IAccountManagementService>();
            var jobTracker = scope.ServiceProvider.GetRequiredService<IStudentImportJobTracker>();

            try
            {
                await using var jobStream = new MemoryStream(fileBytes, writable: false);
                var progress = new Progress<StudentImportProgress>(update =>
                    jobTracker.ReportRowProcessed(jobId, update.ProcessedRows, update.TotalRows));

                var result = await accountManagementService.ImportStudentsAsync(
                    jobStream,
                    fileName,
                    progress,
                    stoppingToken);

                jobTracker.Complete(jobId, result);
            }
            catch (Exception exception)
            {
                jobTracker.Fail(jobId, exception.Message);
            }
        }, stoppingToken);

        await LoadAccountsAsync(cancellationToken);
        return Page();
    }

    public IActionResult OnGetImportStatusAsync(Guid jobId)
    {
        var status = _importJobTracker.GetStatus(jobId);
        return status is null ? NotFound() : new JsonResult(status);
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
