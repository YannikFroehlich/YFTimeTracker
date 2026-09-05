using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data.Tests.Repositories;

[TestClass]
public sealed class NotificationLogRepositoryTests
{
    [TestMethod]
    public async Task AddAsync_persists_entries_ordered_by_most_recent_first()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new NotificationLogRepository(factory);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.PlaytimeLimitReached,
            Title = "Tageslimit erreicht",
            Message = "Test Game: 60 Minuten erreicht.",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T10:00:00Z"),
            RelatedGameId = 1
        }, CancellationToken.None);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.UpdateAvailable,
            Title = "Update verfügbar",
            Message = "YFTimeTracker 0.15.0 steht bereit.",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T11:00:00Z")
        }, CancellationToken.None);

        var recent = await repository.GetRecentAsync(10, CancellationToken.None);

        Assert.HasCount(2, recent);
        Assert.AreEqual(NotificationKind.UpdateAvailable, recent[0].Kind);
        Assert.AreEqual(NotificationKind.PlaytimeLimitReached, recent[1].Kind);
        Assert.AreEqual(1, recent[1].RelatedGameId);
    }

    [TestMethod]
    public async Task GetRecentAsync_limits_result_count()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new NotificationLogRepository(factory);
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(new NotificationLogEntry
            {
                Kind = NotificationKind.UpdateAvailable,
                Title = $"Update {i}",
                Message = "Test",
                CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T10:00:00Z").AddMinutes(i)
            }, CancellationToken.None);
        }

        var recent = await repository.GetRecentAsync(2, CancellationToken.None);

        Assert.HasCount(2, recent);
        Assert.AreEqual("Update 4", recent[0].Title);
        Assert.AreEqual("Update 3", recent[1].Title);
    }

    [TestMethod]
    public async Task MarkAllAsReadAsync_clears_unread_count()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new NotificationLogRepository(factory);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.PlaytimeLimitReached,
            Title = "Tageslimit erreicht",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T10:00:00Z")
        }, CancellationToken.None);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.UpdateAvailable,
            Title = "Update verfügbar",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T11:00:00Z")
        }, CancellationToken.None);

        Assert.AreEqual(2, await repository.GetUnreadCountAsync(CancellationToken.None));

        await repository.MarkAllAsReadAsync(CancellationToken.None);

        Assert.AreEqual(0, await repository.GetUnreadCountAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteAsync_removes_only_the_matching_entry()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new NotificationLogRepository(factory);
        var toDelete = await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.UpdateAvailable,
            Title = "Update verfügbar",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T10:00:00Z")
        }, CancellationToken.None);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.PlaytimeLimitReached,
            Title = "Tageslimit erreicht",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T11:00:00Z")
        }, CancellationToken.None);

        await repository.DeleteAsync(toDelete.Id, CancellationToken.None);

        var remaining = await repository.GetRecentAsync(10, CancellationToken.None);
        Assert.HasCount(1, remaining);
        Assert.AreEqual(NotificationKind.PlaytimeLimitReached, remaining[0].Kind);
    }

    [TestMethod]
    public async Task ClearAllAsync_removes_every_entry()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new NotificationLogRepository(factory);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.UpdateAvailable,
            Title = "Update verfügbar",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T10:00:00Z")
        }, CancellationToken.None);
        await repository.AddAsync(new NotificationLogEntry
        {
            Kind = NotificationKind.PlaytimeLimitReached,
            Title = "Tageslimit erreicht",
            Message = "Test",
            CreatedAtUtc = DateTimeOffset.Parse("2026-09-03T11:00:00Z")
        }, CancellationToken.None);

        await repository.ClearAllAsync(CancellationToken.None);

        Assert.IsEmpty(await repository.GetRecentAsync(10, CancellationToken.None));
    }
}
