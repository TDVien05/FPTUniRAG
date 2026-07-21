using FPTUniRAG.BusinessLayer.Rag.Embeddings;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class EmbeddingSettingsModel : PageModel
{
    private readonly IEmbeddingConfigurationService _configurationService;
    private readonly ILogger<EmbeddingSettingsModel> _logger;

    public EmbeddingSettingsModel(
        IEmbeddingConfigurationService configurationService,
        ILogger<EmbeddingSettingsModel> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    [BindProperty]
    public string SelectedModel { get; set; } = string.Empty;

    [BindProperty]
    [Range(1, int.MaxValue, ErrorMessage = "Fixed chunk size must be greater than zero.")]
    public int FixedChunkSize { get; set; } = 800;

    public IReadOnlyList<EmbeddingModelOption> AvailableModels { get; private set; } = [];

    public EmbeddingConfigurationSnapshot? Current { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        AvailableModels = _configurationService.GetAvailableModels();

        if (!ModelState.IsValid)
        {
            Current = await _configurationService.GetCurrentAsync(cancellationToken);
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId))
        {
            ErrorMessage = "Your admin session is invalid. Please sign in again.";
            return RedirectToPage();
        }

        try
        {
            var current = await _configurationService.UpdateAsync(SelectedModel, FixedChunkSize, adminUserId, cancellationToken);
            SuccessMessage = $"Embedding model changed to {current.Model} with a fixed chunk size of {current.FixedChunkSize:N0}. New uploads and fixed-size subjects will use these values.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to update embedding model configuration for admin {AdminUserId}", adminUserId);
            ErrorMessage = exception is ArgumentException
                ? "Please choose one of the supported embedding models and a valid fixed chunk size."
                : "Unable to save embedding settings right now. Apply the embedding settings SQL script and try again.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        AvailableModels = _configurationService.GetAvailableModels();
        Current = await _configurationService.GetCurrentAsync(cancellationToken);
        SelectedModel = Current.Model;
        FixedChunkSize = Current.FixedChunkSize;
    }
}
