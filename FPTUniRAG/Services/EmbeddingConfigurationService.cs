using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FPTUniRAG.Services;

public sealed class EmbeddingConfigurationService : IEmbeddingConfigurationService
{
    private static readonly IReadOnlyList<EmbeddingModelOption> AvailableModels =
    [
        new("google/gemini-embedding-001", 1536),
        new("openai/text-embedding-3-small", 1536),
        new("baai/bge-m3", 1024)
    ];

    private readonly AppDbContext _dbContext;
    private readonly RagIngestionOptions _options;

    public EmbeddingConfigurationService(
        AppDbContext dbContext,
        IOptions<RagIngestionOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public IReadOnlyList<EmbeddingModelOption> GetAvailableModels() => AvailableModels;

    public async Task<EmbeddingConfigurationSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var setting = await _dbContext.EmbeddingSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);

            if (setting is not null && TryResolveModel(setting.EmbeddingModel, out var model))
            {
                return new EmbeddingConfigurationSnapshot(
                    model.Model,
                    model.Dimensions,
                    setting.UpdatedAt,
                    setting.UpdatedBy,
                    true);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The app can still start before the optional admin-settings script is applied.
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

        var setting = await _dbContext.EmbeddingSettings
            .SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        if (setting is null)
        {
            setting = new EmbeddingSetting { SettingId = 1 };
            _dbContext.EmbeddingSettings.Add(setting);
        }

        setting.EmbeddingModel = selectedModel.Model;
        setting.EmbeddingDimensions = selectedModel.Dimensions;
        setting.UpdatedAt = now;
        setting.UpdatedBy = adminUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

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
