using FPTUniRAG.BusinessLayer.Common;
using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Models;

public sealed class ChatModelConfigurationService : IChatModelConfigurationService
{
    private readonly IChatModelRepository _repository;
    private readonly IOpenRouterModelCatalogService _catalogService;
    private readonly RagIngestionOptions _options;
    private readonly ILogger<ChatModelConfigurationService> _logger;

    public ChatModelConfigurationService(
        IChatModelRepository repository,
        IOpenRouterModelCatalogService catalogService,
        IOptions<RagIngestionOptions> options,
        ILogger<ChatModelConfigurationService> logger)
    {
        _repository = repository;
        _catalogService = catalogService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChatModelDto>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _repository.GetModelsAsync(cancellationToken);
        return models
            .Select(model => new ChatModelDto(model.ChatModelId, model.ModelName, model.DisplayName,
                model.ContextLength, model.IsSelected, model.CreatedAt))
            .ToArray();
    }

    public async Task<ActiveChatModel> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
        var selected = await _repository.GetSelectedAsync(cancellationToken);
        return selected is not null
            ? new ActiveChatModel(selected.ModelName, true)
            : new ActiveChatModel(_options.OpenRouter.ChatModel, false);
    }

    public async Task<OperationResult> AddAsync(string slug, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var normalized = slug?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return OperationResult.Failure("Enter an OpenRouter model name, for example openai/gpt-4o-mini.");
        }

        if (await _repository.ExistsAsync(normalized, cancellationToken))
        {
            return OperationResult.Failure($"{normalized} has already been added.");
        }

        OpenRouterCatalogModel? catalogModel;
        try
        {
            catalogModel = await _catalogService.FindModelAsync(normalized, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to reach the OpenRouter model catalog while adding {Model}", normalized);
            return OperationResult.Failure("Could not reach OpenRouter to verify that model. Check the API key and try again.");
        }

        if (catalogModel is null)
        {
            return OperationResult.Failure($"\"{normalized}\" was not found on OpenRouter. Check the exact model slug and try again.");
        }

        // The first model added becomes the active one so chat never sits in a
        // state where models exist but none is selected.
        var hasSelection = await _repository.GetSelectedAsync(cancellationToken) is not null;
        await _repository.AddAsync(
            catalogModel.Id,
            catalogModel.Name,
            catalogModel.ContextLength,
            selectImmediately: !hasSelection,
            adminUserId,
            cancellationToken);

        return OperationResult.Success(hasSelection
            ? $"{catalogModel.Id} was added."
            : $"{catalogModel.Id} was added and is now the active chat model.");
    }

    public async Task<OperationResult> SelectAsync(Guid chatModelId, CancellationToken cancellationToken = default)
    {
        var selected = await _repository.SelectAsync(chatModelId, cancellationToken);
        return selected
            ? OperationResult.Success("Active chat model updated. New student messages will use it.")
            : OperationResult.Failure("That model no longer exists.");
    }

    public async Task<OperationResult> RemoveAsync(Guid chatModelId, CancellationToken cancellationToken = default)
    {
        var removed = await _repository.RemoveAsync(chatModelId, cancellationToken);
        return removed
            ? OperationResult.Success("Model removed.")
            : OperationResult.Failure("That model no longer exists.");
    }
}
