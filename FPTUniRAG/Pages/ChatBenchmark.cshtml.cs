using FPTUniRAG.BusinessLayer.Rag.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class ChatBenchmarkModel : PageModel
{
    private readonly IChatBenchmarkService _benchmarkService;

    public ChatBenchmarkModel(IChatBenchmarkService benchmarkService)
    {
        _benchmarkService = benchmarkService;
    }

    public IReadOnlyList<ChatBenchmarkRow> ModelRows { get; private set; } = [];

    public IReadOnlyList<ChatSessionBenchmarkRow> SessionRows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var summary = await _benchmarkService.GetSummaryAsync(cancellationToken);
        ModelRows = summary.ModelRows;
        SessionRows = summary.SessionRows;
    }
}
