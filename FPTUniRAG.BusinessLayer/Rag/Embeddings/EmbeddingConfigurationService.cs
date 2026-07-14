using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.DataAccessLayer.Repositories.Embeddings;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public sealed class EmbeddingConfigurationService : IEmbeddingConfigurationService
{
    private static readonly IReadOnlyList<EmbeddingModelOption> AvailableModels =
    [
        new("google/gemini-embedding-001", 1536),
        new("openai/text-embedding-3-small", 1536),
        new("baai/bge-m3", 1024)
    ];

    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly RagIngestionOptions _options;

    public EmbeddingConfigurationService(
        IEmbeddingRepository embeddingRepository,
        IOptions<RagIngestionOptions> options)
    {
        _embeddingRepository = embeddingRepository;
        _options = options.Value;
    }

    public IReadOnlyList<EmbeddingModelOption> GetAvailableModels() => AvailableModels;

    public async Task<EmbeddingConfigurationSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _embeddingRepository.GetSettingAsync(cancellationToken);
        if (setting is not null && TryResolveModel(setting.Model, out var model))
        {
            return new EmbeddingConfigurationSnapshot(model.Model, model.Dimensions, setting.UpdatedAt, setting.UpdatedBy, true);
        }

        var fallback = ResolveFallbackModel();
        return new EmbeddingConfigurationSnapshot(fallback.Model, fallback.Dimensions, null, null, false);
    }

    public async Task<EmbeddingConfigurationSnapshot> UpdateAsync(
        string model,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveModel(model, out var selectedModel))
        {
            throw new ArgumentException("The selected embedding model is not supported.", nameof(model));
        }

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await _embeddingRepository.UpsertSettingAsync(selectedModel.Model, selectedModel.Dimensions, adminUserId, now, cancellationToken);

        return new EmbeddingConfigurationSnapshot(
            selectedModel.Model,
            selectedModel.Dimensions,
            now,
            adminUserId,
            true);
    }

    private EmbeddingModelOption ResolveFallbackModel()
    {
        return TryResolveModel(_options.OpenRouter.EmbeddingModel, out var configuredModel)
            ? configuredModel
            : AvailableModels[0];
    }

    private static bool TryResolveModel(string? model, out EmbeddingModelOption selectedModel)
    {
        selectedModel = AvailableModels.FirstOrDefault(item =>
            string.Equals(item.Model, model?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return selectedModel is not null;
    }
}
