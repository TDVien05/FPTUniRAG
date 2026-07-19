namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public interface IChatModelRepository
{
    Task<IReadOnlyList<ChatModelRecord>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<ChatModelRecord?> GetSelectedAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string modelName, CancellationToken cancellationToken = default);

    Task<ChatModelRecord> AddAsync(
        string modelName,
        string? displayName,
        int? contextLength,
        bool selectImmediately,
        Guid createdBy,
        CancellationToken cancellationToken = default);

    Task<bool> SelectAsync(Guid chatModelId, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid chatModelId, CancellationToken cancellationToken = default);
}

public sealed record ChatModelRecord(
    Guid ChatModelId,
    string ModelName,
    string? DisplayName,
    int? ContextLength,
    bool IsSelected,
    DateTime CreatedAt);
