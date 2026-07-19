using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public sealed class ChatModelRepository(AppDbContext context) : IChatModelRepository
{
    public async Task<IReadOnlyList<ChatModelRecord>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ChatModels.AsNoTracking()
                .OrderByDescending(model => model.IsSelected)
                .ThenBy(model => model.ModelName)
                .Select(model => new ChatModelRecord(model.ChatModelId, model.ModelName, model.DisplayName,
                    model.ContextLength, model.IsSelected, model.CreatedAt))
                .ToListAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }
    }

    public async Task<ChatModelRecord?> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ChatModels.AsNoTracking()
                .Where(model => model.IsSelected)
                .Select(model => new ChatModelRecord(model.ChatModelId, model.ModelName, model.DisplayName,
                    model.ContextLength, model.IsSelected, model.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Schema has not been applied yet; callers fall back to the configured model.
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string modelName, CancellationToken cancellationToken = default) =>
        await context.ChatModels.AnyAsync(model => model.ModelName == modelName, cancellationToken);

    public async Task<ChatModelRecord> AddAsync(
        string modelName,
        string? displayName,
        int? contextLength,
        bool selectImmediately,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        if (selectImmediately)
        {
            await ClearSelectionAsync(cancellationToken);
        }

        var model = new ChatModel
        {
            ChatModelId = Guid.NewGuid(),
            ModelName = modelName,
            DisplayName = displayName,
            ContextLength = contextLength,
            IsSelected = selectImmediately,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            CreatedBy = createdBy
        };

        context.ChatModels.Add(model);
        await context.SaveChangesAsync(cancellationToken);

        return new ChatModelRecord(model.ChatModelId, model.ModelName, model.DisplayName,
            model.ContextLength, model.IsSelected, model.CreatedAt);
    }

    public async Task<bool> SelectAsync(Guid chatModelId, CancellationToken cancellationToken = default)
    {
        if (!await context.ChatModels.AnyAsync(model => model.ChatModelId == chatModelId, cancellationToken))
        {
            return false;
        }

        // The partial unique index permits a single selected row, so the old selection
        // must be cleared before the new one is set — hence two ordered statements
        // rather than tracked updates, whose order EF does not guarantee.
        await ClearSelectionAsync(cancellationToken);
        await context.ChatModels
            .Where(model => model.ChatModelId == chatModelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(model => model.IsSelected, true), cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(Guid chatModelId, CancellationToken cancellationToken = default)
    {
        var target = await context.ChatModels.FirstOrDefaultAsync(model => model.ChatModelId == chatModelId, cancellationToken);
        if (target is null)
        {
            return false;
        }

        context.ChatModels.Remove(target);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ClearSelectionAsync(CancellationToken cancellationToken) =>
        await context.ChatModels
            .Where(model => model.IsSelected)
            .ExecuteUpdateAsync(setters => setters.SetProperty(model => model.IsSelected, false), cancellationToken);
}
