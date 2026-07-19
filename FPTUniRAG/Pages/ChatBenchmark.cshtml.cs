using FPTUniRAG.BusinessLayer.Rag.Chat;
using FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;
using FPTUniRAG.BusinessLayer.Rag.Chat.Models;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class ChatBenchmarkModel : PageModel
{
    private const int SubjectLimit = 200;
    private const int BatchHistoryLimit = 20;

    private readonly IChatBenchmarkService _benchmarkService;
    private readonly IChatBenchmarkRunner _benchmarkRunner;
    private readonly IChatModelConfigurationService _chatModelConfiguration;
    private readonly IStudentChatRepository _chatRepository;

    public ChatBenchmarkModel(
        IChatBenchmarkService benchmarkService,
        IChatBenchmarkRunner benchmarkRunner,
        IChatModelConfigurationService chatModelConfiguration,
        IStudentChatRepository chatRepository)
    {
        _benchmarkService = benchmarkService;
        _benchmarkRunner = benchmarkRunner;
        _chatModelConfiguration = chatModelConfiguration;
        _chatRepository = chatRepository;
    }

    [BindProperty]
    public string PromptText { get; set; } = string.Empty;

    [BindProperty]
    public Guid RunSubjectId { get; set; }

    [BindProperty]
    public List<string> SelectedModels { get; set; } = [];

    public IReadOnlyList<ChatBenchmarkRow> ModelRows { get; private set; } = [];

    public IReadOnlyList<ChatSessionBenchmarkRow> SessionRows { get; private set; } = [];

    public IReadOnlyList<ChatBenchmarkRunSummary> RunSummaries { get; private set; } = [];

    /// <summary>Which past benchmark to display. Defaults to the most recent one.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? BatchId { get; set; }

    public IReadOnlyList<ChatBenchmarkBatchRecord> Batches { get; private set; } = [];

    public ChatBenchmarkBatchRecord? SelectedBatch { get; private set; }

    public IReadOnlyList<ChatModelDto> AvailableModels { get; private set; } = [];

    public IReadOnlyList<ChatSubjectRecord> Subjects { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminUserId))
        {
            ErrorMessage = "Your admin session is invalid. Please sign in again.";
            return RedirectToPage();
        }

        var result = await _benchmarkRunner.StartAsync(RunSubjectId, SelectedModels, PromptText, adminUserId, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.Message;
            return RedirectToPage();
        }

        SuccessMessage = result.Message;
        return RedirectToPage();
    }

    /// <summary>Polled by the page while runs are in flight.</summary>
    public async Task<IActionResult> OnGetRunStatusAsync(CancellationToken cancellationToken)
    {
        // Always polls the newest batch: that is the one still running.
        var summaries = await _benchmarkService.GetBatchSummariesAsync(null, cancellationToken);
        return new JsonResult(summaries.Select(summary => new
        {
            runId = summary.RunId,
            model = summary.ModelName,
            status = summary.Status,
            progressPercent = summary.ProgressPercent,
            completedCount = summary.CompletedCount,
            promptCount = summary.PromptCount,
            isFinished = summary.IsFinished
        }));
    }

    private bool TryGetAdminId(out Guid adminUserId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminUserId);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var summary = await _benchmarkService.GetSummaryAsync(cancellationToken);
        ModelRows = summary.ModelRows;
        SessionRows = summary.SessionRows;

        Batches = await _benchmarkService.GetRecentBatchesAsync(BatchHistoryLimit, cancellationToken);
        SelectedBatch = BatchId.HasValue
            ? Batches.FirstOrDefault(batch => batch.BatchId == BatchId.Value)
            : Batches.FirstOrDefault();

        // Run summaries already carry their result rows, so answers render inline with
        // no drill-down round trip.
        RunSummaries = await _benchmarkService.GetBatchSummariesAsync(SelectedBatch?.BatchId, cancellationToken);
        AvailableModels = await _chatModelConfiguration.GetModelsAsync(cancellationToken);
        Subjects = await _chatRepository.SearchSubjectsAsync(null, SubjectLimit, cancellationToken);
    }

    public bool IsViewingLatestBatch =>
        Batches.Count == 0 || SelectedBatch is null || SelectedBatch.BatchId == Batches[0].BatchId;

    public bool HasRunInFlight => RunSummaries.Any(run => !run.IsFinished);
}
