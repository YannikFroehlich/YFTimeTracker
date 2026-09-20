using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Data.Sync;

/// <summary>
/// Die lokale Seite des Kontoabgleichs: liest den Bestand als Abgleich-Sicht und
/// schreibt uebernommene Aenderungen zurueck.
///
/// Die geraeteunabhaengige Identitaet wird bei jedem Lauf neu aus dem Inhalt
/// berechnet statt gespeichert. Damit findet ein umbenanntes manuelles Spiel
/// automatisch wieder zu seinem Gegenstueck, und es gibt keine zweite Wahrheit,
/// die mit den Daten aus dem Tritt geraten koennte.
/// </summary>
public sealed partial class AccountSyncStore(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory,
    IClock clock) : IAccountSyncStore
{
    /// <summary>
    /// Einstellungen, die an diesen PC gebunden sind und deshalb nicht ins Konto
    /// gehoeren. Wanderte etwa der Autostart mit, schaltete er sich auf dem
    /// anderen Rechner ungefragt mit ein.
    /// </summary>
    private static readonly HashSet<string> DeviceLocalSettingKeys = new(StringComparer.Ordinal)
    {
        AppSettingKeys.CloudUserEmail,
        AppSettingKeys.CloudDeviceId,
        AppSettingKeys.CloudLastSyncUtc,
        AppSettingKeys.CloudKnownDevices,
        AppSettingKeys.BackupExternalFolderPath,
        AppSettingKeys.BackupDestination,
        AppSettingKeys.LastBackupDate,
        AppSettingKeys.StartupEnabled,
        AppSettingKeys.FirstRunSetupCompleted,
        AppSettingKeys.LastSeenChangelogHeading,
        AppSettingKeys.LastLoggedUpdateVersion,
        AppSettingKeys.UpdateRemindVersion,
        AppSettingKeys.UpdateRemindAfterUtc,
        AppSettingKeys.GlobalSearchRecentQueries,

        // Name und Farbe wandern als Profil ins Konto, nicht als Einstellung.
        AppSettingKeys.ProfileDisplayName,
        AppSettingKeys.ProfileAccentColor
    };

    public async Task<LocalSyncSnapshot> ReadAsync(string machineKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var games = await context.Games.AsNoTracking().OrderBy(game => game.Id).ToListAsync(cancellationToken);
        var executables = await context.GameExecutables.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var tags = await context.GameTags.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var sessions = await context.GameSessions.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var artworks = await context.GameArtworks.AsNoTracking().OrderBy(item => item.GameId).ToListAsync(cancellationToken);
        var exclusions = await context.TrackingExclusionRules.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var settings = await context.AppSettings.AsNoTracking().OrderBy(item => item.Key).ToListAsync(cancellationToken);
        var tombstones = await context.SyncTombstones.AsNoTracking().ToListAsync(cancellationToken);

        // Einmal abgeglichene Datensaetze behalten ihre gespeicherte Identitaet.
        // Neu berechnet wuerde sich etwa die einer Session aendern, sobald sie auf
        // einem zweiten PC liegt - ihre Identitaet enthaelt den MachineKey des
        // Ursprungsgeraets. Sie wuerde dann als neu gelten und ein zweites Mal
        // hochgeladen.
        var gameIdentityById = games.ToDictionary(game => game.Id, SyncIdentity.ForGame);

        var gameSet = BuildSet(
            games,
            game => game.CloudIdentity ?? gameIdentityById[game.Id],
            SyncIdentity.HashGame,
            game => game.Id,
            game => game.CloudId,
            game => game.SyncedHash,
            (game, identity, hash) => new CloudGame(
                identity, game.CloudId, hash, game.Name, game.Source, game.ExternalGameId,
                game.AddedAtUtc, game.DailyPlaytimeLimitMinutes, game.WeeklyPlaytimeLimitMinutes, game.IsPinned,
                game.BaselinePlaytimeMinutes));

        var executableSet = BuildSet(
            executables.Where(item => gameIdentityById.ContainsKey(item.GameId)).ToList(),
            item => item.CloudIdentity ?? SyncIdentity.ForExecutable(gameIdentityById[item.GameId], item),
            SyncIdentity.HashExecutable,
            item => item.Id,
            item => item.CloudId,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudExecutable(
                identity, item.CloudId, hash, gameIdentityById[item.GameId],
                item.ExecutablePath, item.ExecutablePathKey, item.ExecutableName, item.IsPrimary, item.AddedAtUtc));

        var tagSet = BuildSet(
            tags.Where(item => gameIdentityById.ContainsKey(item.GameId)).ToList(),
            item => item.CloudIdentity ?? SyncIdentity.ForTag(gameIdentityById[item.GameId], item),
            SyncIdentity.HashTag,
            item => item.Id,
            item => item.CloudId,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudTag(
                identity, item.CloudId, hash, gameIdentityById[item.GameId], item.Tag));

        var sessionSet = BuildSet(
            sessions.Where(item => gameIdentityById.ContainsKey(item.GameId)).ToList(),
            item => item.CloudIdentity ?? SyncIdentity.ForSession(machineKey, gameIdentityById[item.GameId], item),
            SyncIdentity.HashSession,
            item => item.Id,
            item => item.CloudId,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudGameSession(
                identity, item.CloudId, hash, gameIdentityById[item.GameId],
                item.StartedAtUtc, item.LastSeenAtUtc, item.EndedAtUtc, item.DurationSeconds, item.BootSessionId));

        var artworkSet = BuildSet(
            artworks.Where(item => gameIdentityById.ContainsKey(item.GameId)).ToList(),
            item => item.CloudIdentity ?? SyncIdentity.ForArtwork(gameIdentityById[item.GameId]),
            SyncIdentity.HashArtwork,
            item => item.GameId,
            item => item.CloudId,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudArtwork(
                identity, item.CloudId, hash, gameIdentityById[item.GameId],
                item.ContentType, item.FileExtension, item.Sha256, string.Empty, item.UpdatedAtUtc, item.ImageData));

        var exclusionSet = BuildSet(
            exclusions,
            item => item.CloudIdentity ?? SyncIdentity.ForExclusion(item),
            SyncIdentity.HashExclusion,
            item => item.Id,
            item => item.CloudId,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudExclusion(
                identity, item.CloudId, hash, item.Kind, item.Value, item.ValueKey, item.AddedAtUtc));

        var portableSettings = settings.Where(item => !DeviceLocalSettingKeys.Contains(item.Key)).ToList();
        var settingSet = BuildSet(
            portableSettings,
            item => SyncIdentity.ForSetting(item.Key),
            item => SyncIdentity.HashSetting(item.Key, item.Value),
            _ => 0,
            _ => null,
            item => item.SyncedHash,
            (item, identity, hash) => new CloudSetting(
                identity, null, hash, item.Key, item.Value, item.UpdatedAtUtc));

        var profile = new CloudProfile(
            settings.FirstOrDefault(item => item.Key == AppSettingKeys.ProfileDisplayName)?.Value,
            settings.FirstOrDefault(item => item.Key == AppSettingKeys.ProfileAccentColor)?.Value,
            clock.UtcNow);

        return new LocalSyncSnapshot(
            gameSet, executableSet, tagSet, sessionSet, exclusionSet, settingSet, artworkSet,
            tombstones.Select(item => new LocalTombstone(item.Identity, item.CloudId, item.DeletedAtUtc)).ToList(),
            profile);
    }

    private static LocalSyncSet<TCloud> BuildSet<TEntity, TCloud>(
        IReadOnlyList<TEntity> items,
        Func<TEntity, string> identity,
        Func<TEntity, string> contentHash,
        Func<TEntity, long> localId,
        Func<TEntity, string?> cloudId,
        Func<TEntity, string?> syncedHash,
        Func<TEntity, string, string, TCloud> toCloud)
    {
        var entries = new List<LocalSyncEntry>(items.Count);
        var byIdentity = new Dictionary<string, TCloud>(items.Count, StringComparer.Ordinal);

        foreach (var item in items)
        {
            var itemIdentity = identity(item);
            var hash = contentHash(item);

            // Zwei lokale Datensaetze mit derselben Identitaet (etwa zwei manuelle
            // Spiele gleichen Namens) waeren im Konto nicht unterscheidbar. Der
            // erste gewinnt; der zweite bleibt lokal unangetastet.
            if (!byIdentity.TryAdd(itemIdentity, toCloud(item, itemIdentity, hash)))
            {
                continue;
            }

            entries.Add(new LocalSyncEntry(itemIdentity, localId(item), cloudId(item), hash, syncedHash(item)));
        }

        return new LocalSyncSet<TCloud>(entries, byIdentity);
    }
}
