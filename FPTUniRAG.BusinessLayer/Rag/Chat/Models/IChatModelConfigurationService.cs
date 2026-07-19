using FPTUniRAG.BusinessLayer.Common;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Models;

public interface IChatModelConfigurationService
{
    Task<IReadOnlyList<ChatModelDto>> GetModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Model the student chat feature should use right now. Falls back to
    /// RagIngestion:OpenRouter:ChatModel when no model has been selected.
    /// </summary>
    Task<ActiveChatModel> GetActiveModelAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> AddAsync(string slug, Guid adminUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> SelectAsync(Guid chatModelId, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAsync(Guid chatModelId, CancellationToken cancellationToken = default);
}

public sealed record ChatModelDto(
    Guid ChatModelId,
    string ModelName,
    string? DisplayName,
    int? ContextLength,
    bool IsSelected,
    DateTime CreatedAt);

public sealed record ActiveChatModel(string ModelName, bool IsFromDatabase);
