using FPTUniRAG.BusinessLayer.Rag.Chat.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class ChatModelsModel : PageModel
{
    private readonly IChatModelConfigurationService _configurationService;

    public ChatModelsModel(IChatModelConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    [BindProperty]
    public string ModelSlug { get; set; } = string.Empty;

    public IReadOnlyList<ChatModelDto> Models { get; private set; } = [];

    public ActiveChatModel? ActiveModel { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminUserId))
        {
            ErrorMessage = "Your admin session is invalid. Please sign in again.";
            return RedirectToPage();
        }

        var result = await _configurationService.AddAsync(ModelSlug, adminUserId, cancellationToken);
        AssignFeedback(result.Succeeded, result.Message);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSelectAsync(Guid chatModelId, CancellationToken cancellationToken)
    {
        var result = await _configurationService.SelectAsync(chatModelId, cancellationToken);
        AssignFeedback(result.Succeeded, result.Message);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid chatModelId, CancellationToken cancellationToken)
    {
        var result = await _configurationService.RemoveAsync(chatModelId, cancellationToken);
        AssignFeedback(result.Succeeded, result.Message);
        return RedirectToPage();
    }

    private void AssignFeedback(bool succeeded, string message)
    {
        if (succeeded)
        {
            SuccessMessage = message;
        }
        else
        {
            ErrorMessage = message;
        }
    }

    private bool TryGetAdminId(out Guid adminUserId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminUserId);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Models = await _configurationService.GetModelsAsync(cancellationToken);
        ActiveModel = await _configurationService.GetActiveModelAsync(cancellationToken);
    }
}
