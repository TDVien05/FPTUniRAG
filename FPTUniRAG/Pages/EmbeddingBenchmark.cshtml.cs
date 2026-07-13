using FPTUniRAG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class EmbeddingBenchmarkModel : PageModel
{
    private readonly IEmbeddingBenchmarkService _benchmarkService;

    public EmbeddingBenchmarkModel(IEmbeddingBenchmarkService benchmarkService)
    {
        _benchmarkService = benchmarkService;
    }

    public IReadOnlyList<EmbeddingBenchmarkRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await _benchmarkService.GetSummaryAsync(cancellationToken);
    }
}
