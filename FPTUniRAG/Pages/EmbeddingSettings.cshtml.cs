using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Services;
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId))
        {
            ErrorMessage = "Your admin session is invalid. Please sign in again.";
            return RedirectToPage();
        }

        try
        {
            var current = await _configurationService.UpdateAsync(SelectedModel, adminUserId, cancellationToken);
            SuccessMessage = $"Embedding model changed to {current.Model}. New uploads will use this model.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to update embedding model configuration for admin {AdminUserId}", adminUserId);
            ErrorMessage = exception is ArgumentException
                ? "Please choose one of the supported embedding models."
                : "Unable to save embedding settings right now. Apply the embedding settings SQL script and try again.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        AvailableModels = _configurationService.GetAvailableModels();
        Current = await _configurationService.GetCurrentAsync(cancellationToken);
        SelectedModel = Current.Model;
    }
}
