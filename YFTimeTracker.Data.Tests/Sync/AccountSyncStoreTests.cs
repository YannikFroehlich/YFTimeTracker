using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;
using YFTimeTracker.Data.Sync;
using TestRepositories = YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Sync;

[TestClass]
public sealed class AccountSyncStoreTests
{
    private const string MachineKey = "machine-a";

    private static async Task<(TestRepositories.TempAppPathProvider Paths, TestRepositories.TestDbContextFactory Factory, TestRepositories.TestClock Clock, AccountSyncStore Store)>
        CreateAsync()
    {
        var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-15T20:00:00Z"));
        return (paths, factory, clock, new AccountSyncStore(factory, clock));
    }

    private static async Task<Game> AddGameWithSessionAsync(
        TestRepositories.TestDbContextFactory factory,
        TestRepositories.TestClock clock)
    {
        var game = await new GameRepository(factory).AddAsync(new Game
        {
            Name = "Rennspiel",
            ExecutablePath = @"C:\Games\Rennspiel\game.exe",
            ExecutablePathKey = @"C:\GAMES\RENNSPIEL\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        await using var context = factory.CreateDbContext();
        context.GameSessions.Add(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = clock.UtcNow,
            LastSeenAtUtc = clock.UtcNow.AddHours(1),
            EndedAtUtc = clock.UtcNow.AddHours(1),
            DurationSeconds = 3600,
            BootSessionId = "202609152000"
        });
        await context.SaveChangesAsync();
        return game;
    }

    /// <summary>
    /// Baut das, was der Abgleich nach einem erfolgreichen Hochladen zurueckgibt:
    /// zu jeder Identitaet den erreichten Stand.
    /// </summary>
    private static AccountApply ApplyFrom(LocalSyncSnapshot local) => new(
        AccountSnapshot.Empty,
        new Dictionary<SyncEntityKind, IReadOnlyList<string>>(),
        new Dictionary<SyncEntityKind, IReadOnlyDictionary<string, SyncStamp>>
        {
            [SyncEntityKind.Game] = local.Games.Entries.ToDictionary(
                entry => entry.Identity, entry => new SyncStamp(null, entry.ContentHash)),
            [SyncEntityKind.Session] = local.Sessions.Entries.ToDictionary(
                entry => entry.Identity, entry => new SyncStamp(null, entry.ContentHash)),
            [SyncEntityKind.Executable] = local.Executables.Entries.ToDictionary(
                entry => entry.Identity, entry => new SyncStamp(null, entry.ContentHash))
        },
        [],
        MachineKey);

    [TestMethod]
    public async Task Sessions_keep_their_identity_after_a_sync()
    {
        var (paths, factory, clock, store) = await CreateAsync();
        using var _ = paths;

        await AddGameWithSessionAsync(factory, clock);

        var local = await store.ReadAsync(MachineKey, CancellationToken.None);
        await store.ApplyAsync(ApplyFrom(local), CancellationToken.None);

        await using var context = factory.CreateDbContext();
        var session = await context.GameSessions.SingleAsync();

        // Ohne gespeicherte Identität würde die Session bei jedem Lauf erneut
        // hochgeladen, und ihr Löschen erzeugte keinen Grabstein.
        Assert.IsNotNull(session.CloudIdentity);
        Assert.IsNotNull(session.SyncedHash);
        StringAssert.StartsWith(session.CloudIdentity, $"ses:{MachineKey}:");
    }

    [TestMethod]
    public async Task A_synced_session_counts_as_unchanged_on_the_next_read()
    {
        var (paths, factory, clock, store) = await CreateAsync();
        using var _ = paths;

        await AddGameWithSessionAsync(factory, clock);

        var first = await store.ReadAsync(MachineKey, CancellationToken.None);
        await store.ApplyAsync(ApplyFrom(first), CancellationToken.None);

        var second = await store.ReadAsync(MachineKey, CancellationToken.None);

        Assert.IsFalse(
            second.Sessions.Entries.Single().HasChangedLocally,
            "Eine unveränderte Session darf nicht bei jedem Abgleich erneut hochgeladen werden.");
    }

    [TestMethod]
    public async Task Deleting_a_synced_session_now_leaves_a_tombstone()
    {
        var (paths, factory, clock, store) = await CreateAsync();
        using var _ = paths;

        await AddGameWithSessionAsync(factory, clock);
        var local = await store.ReadAsync(MachineKey, CancellationToken.None);
        await store.ApplyAsync(ApplyFrom(local), CancellationToken.None);

        long sessionId;
        await using (var context = factory.CreateDbContext())
        {
            sessionId = await context.GameSessions.Select(item => item.Id).SingleAsync();
        }

        await new GameSessionRepository(factory).DeleteAsync(sessionId, CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        Assert.AreEqual(
            1,
            await verify.SyncTombstones.CountAsync(item => item.Kind == SyncEntityKind.Session));
    }

    [TestMethod]
    public async Task Device_local_settings_stay_out_of_the_account()
    {
        var (paths, factory, clock, store) = await CreateAsync();
        using var _ = paths;

        var settings = new SettingsStore(factory, clock);
        await settings.SetAsync(AppSettingKeys.Theme, "Dark", CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.StartupEnabled, "True", CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.CloudUserEmail, "spieler@example.de", CancellationToken.None);

        var local = await store.ReadAsync(MachineKey, CancellationToken.None);
        var keys = local.Settings.ByIdentity.Values.Select(item => item.Key).ToList();

        CollectionAssert.Contains(keys, AppSettingKeys.Theme);

        // Wanderte der Autostart mit, schaltete er sich auf dem anderen PC
        // ungefragt mit ein.
        CollectionAssert.DoesNotContain(keys, AppSettingKeys.StartupEnabled);
        CollectionAssert.DoesNotContain(keys, AppSettingKeys.CloudUserEmail);
    }

    [TestMethod]
    public async Task Profile_name_and_colour_travel_with_the_account()
    {
        var (paths, factory, clock, store) = await CreateAsync();
        using var _ = paths;

        var settings = new SettingsStore(factory, clock);
        await settings.SetAsync(AppSettingKeys.ProfileDisplayName, "Yannik", CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.ProfileAccentColor, "#3182FF", CancellationToken.None);

        var local = await store.ReadAsync(MachineKey, CancellationToken.None);

        Assert.AreEqual("Yannik", local.Profile.DisplayName);
        Assert.AreEqual("#3182FF", local.Profile.AccentColor);

        // Als Einstellung wären sie doppelt unterwegs; sie gehören ins Profil.
        CollectionAssert.DoesNotContain(
            local.Settings.ByIdentity.Values.Select(item => item.Key).ToList(),
            AppSettingKeys.ProfileDisplayName);
    }
}
