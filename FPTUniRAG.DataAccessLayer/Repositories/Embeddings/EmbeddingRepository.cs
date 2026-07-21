using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FPTUniRAG.DataAccessLayer.Repositories.Embeddings;

public sealed class EmbeddingRepository(AppDbContext context) : IEmbeddingRepository
{
    public async Task<EmbeddingSettingRecord?> GetSettingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.EmbeddingSettings.AsNoTracking().Where(item => item.SettingId == 1)
                .Select(item => new EmbeddingSettingRecord(item.EmbeddingModel, item.EmbeddingDimensions, item.FixedChunkSize, item.UpdatedAt, item.UpdatedBy))
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    public async Task<EmbeddingSettingRecord> UpsertSettingAsync(string model, int dimensions, int fixedChunkSize, Guid updatedBy, DateTime updatedAt, CancellationToken cancellationToken = default)
    {
        var setting = await context.EmbeddingSettings.SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);
        if (setting is null) { setting = new EmbeddingSetting { SettingId = 1 }; context.EmbeddingSettings.Add(setting); }
        setting.EmbeddingModel = model; setting.EmbeddingDimensions = dimensions; setting.FixedChunkSize = fixedChunkSize; setting.UpdatedAt = updatedAt; setting.UpdatedBy = updatedBy;
        await context.SaveChangesAsync(cancellationToken);
        return new EmbeddingSettingRecord(model, dimensions, fixedChunkSize, updatedAt, updatedBy);
    }

    public async Task<IReadOnlyList<EmbeddingRunRecord>> GetRunsAsync(CancellationToken cancellationToken = default) =>
        await context.DocumentEmbeddingRuns.AsNoTracking().OrderByDescending(run => run.StartedAt)
            .Select(run => new EmbeddingRunRecord(run.EmbeddingModel, run.EmbeddingDimensions, run.DocumentId, run.DocumentSizeBytes,
                run.ChunkCount, run.VectorCount, run.StartedAt, run.CompletedAt, run.Status))
            .ToListAsync(cancellationToken);
}
