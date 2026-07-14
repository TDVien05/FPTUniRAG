using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class FreeTokenQuotaService : IFreeTokenQuotaService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<FreeTokenQuotaService> _logger;

    public FreeTokenQuotaService(AppDbContext dbContext, ILogger<FreeTokenQuotaService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<long> GetMonthlyTokenLimitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var setting = await _dbContext.StudentFreeQuotaSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);

            return setting?.MonthlyTokenLimit > 0
                ? setting.MonthlyTokenLimit
                : IFreeTokenQuotaService.DefaultMonthlyTokenLimit;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load free student token limit. Using default {DefaultLimit}.", IFreeTokenQuotaService.DefaultMonthlyTokenLimit);
            return IFreeTokenQuotaService.DefaultMonthlyTokenLimit;
        }
    }

    public async Task<long> UpdateMonthlyTokenLimitAsync(
        long monthlyTokenLimit,
        Guid updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (monthlyTokenLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyTokenLimit), "Free monthly token limit must be greater than zero.");
        }

        var setting = await _dbContext.StudentFreeQuotaSettings
            .SingleOrDefaultAsync(item => item.SettingId == 1, cancellationToken);

        if (setting is null)
        {
            setting = new StudentFreeQuotaSetting { SettingId = 1 };
            _dbContext.StudentFreeQuotaSettings.Add(setting);
        }

        setting.MonthlyTokenLimit = monthlyTokenLimit;
        setting.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        setting.UpdatedBy = updatedBy;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return setting.MonthlyTokenLimit;
    }
}
