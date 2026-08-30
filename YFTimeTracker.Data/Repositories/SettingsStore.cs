using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class SettingsStore(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory,
    IClock clock) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AppSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.AppSettings.FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);
        if (existing is null)
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = clock.UtcNow
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        var raw = await GetAsync(key, cancellationToken);
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken)
    {
        var raw = await GetAsync(key, cancellationToken);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
