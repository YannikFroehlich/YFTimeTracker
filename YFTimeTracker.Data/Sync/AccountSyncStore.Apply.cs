using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Data.Sync;

/// <summary>
/// Der Schreibpfad des Kontoabgleichs. Getrennt gehalten, weil er als einziger
/// Teil bestehende Nutzerdaten veraendert und deshalb eigene Sorgfalt verdient.
/// </summary>
public sealed partial class AccountSyncStore
{
    public async Task ApplyAsync(AccountApply apply, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Alles oder nichts: bricht der Lauf in der Mitte ab, darf kein Spiel ohne
        // seine Sessions und keine Session ohne ihr Spiel zurueckbleiben.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var games = await context.Games.ToListAsync(cancellationToken);
        var gamesByIdentity = BuildIdentityIndex(games, game => game.CloudIdentity ?? SyncIdentity.ForGame(game));

        await ApplyGamesAsync(context, apply, games, gamesByIdentity, cancellationToken);

        // Nach dem Speichern haben neu angelegte Spiele ihre Id; erst jetzt lassen
        // sich Fremdschluessel fuer EXE-Dateien, Tags, Sessions und Cover setzen.
        await context.SaveChangesAsync(cancellationToken);
        gamesByIdentity = BuildIdentityIndex(
            await context.Games.ToListAsync(cancellationToken),
            game => game.CloudIdentity ?? SyncIdentity.ForGame(game));

        await ApplyExecutablesAsync(context, apply, gamesByIdentity, cancellationToken);
        await ApplyTagsAsync(context, apply, gamesByIdentity, cancellationToken);
        await ApplySessionsAsync(context, apply, gamesByIdentity, cancellationToken);
        await ApplyArtworkAsync(context, apply, gamesByIdentity, cancellationToken);
        await ApplyExclusionsAsync(context, apply, cancellationToken);
        await ApplySettingsAsync(context, apply, cancellationToken);
        await ApplyProfileAsync(context, apply, cancellationToken);
        await ResolveTombstonesAsync(context, apply, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Dictionary<string, T> BuildIdentityIndex<T>(IEnumerable<T> items, Func<T, string> identity)
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            index.TryAdd(identity(item), item);
        }

        return index;
    }

    private static SyncStamp? StampFor(AccountApply apply, SyncEntityKind kind, string identity) =>
        apply.Stamps.TryGetValue(kind, out var stamps) && stamps.TryGetValue(identity, out var stamp)
            ? stamp
            : null;

    private static IReadOnlyList<string> RemovalsFor(AccountApply apply, SyncEntityKind kind) =>
        apply.RemoveIdentities.TryGetValue(kind, out var removals) ? removals : [];

    // -------------------------------------------------------------------------
    // Spiele
    // -------------------------------------------------------------------------

    private static Task ApplyGamesAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        List<Game> games,
        Dictionary<string, Game> byIdentity,
        CancellationToken cancellationToken)
    {
        foreach (var incoming in apply.Upserts.Games)
        {
            if (byIdentity.TryGetValue(incoming.Identity, out var existing))
            {
                existing.Name = incoming.Name;
                existing.Source = incoming.Source;
                existing.ExternalGameId = incoming.ExternalGameId;
                existing.AddedAtUtc = incoming.AddedAtUtc;
                existing.DailyPlaytimeLimitMinutes = incoming.DailyLimitMinutes;
                existing.WeeklyPlaytimeLimitMinutes = incoming.WeeklyLimitMinutes;
                existing.IsPinned = incoming.IsPinned;
                existing.BaselinePlaytimeMinutes = incoming.BaselineMinutes;
                continue;
            }

            var game = new Game
            {
                Name = incoming.Name,
                Source = incoming.Source,
                ExternalGameId = incoming.ExternalGameId,
                AddedAtUtc = incoming.AddedAtUtc,
                DailyPlaytimeLimitMinutes = incoming.DailyLimitMinutes,
                WeeklyPlaytimeLimitMinutes = incoming.WeeklyLimitMinutes,
                IsPinned = incoming.IsPinned,
                BaselinePlaytimeMinutes = incoming.BaselineMinutes,

                // Die Legacy-Spalten sind NOT NULL und der Pfadschluessel ist
                // eindeutig indiziert. Ein Spiel, das von einem anderen PC kommt
                // und hier nicht installiert ist, hat keinen echten Pfad -
                // deshalb ein aus der Identitaet abgeleiteter Platzhalter, der
                // nie auf einen laufenden Prozess passt.
                LegacyExecutablePath = string.Empty,
                LegacyExecutablePathKey = $"cloud:{incoming.Identity}",
                LegacyExecutableName = string.Empty
            };

            context.Games.Add(game);
            byIdentity[incoming.Identity] = game;
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Game))
        {
            if (byIdentity.TryGetValue(identity, out var game))
            {
                context.Games.Remove(game);
                byIdentity.Remove(identity);
            }
        }

        StampAll(apply, SyncEntityKind.Game, byIdentity, (game, identity, stamp) =>
            (game.CloudId, game.CloudIdentity, game.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));

        _ = games;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Vermerkt an jedem beteiligten Datensatz, mit welcher Identitaet und welchem
    /// Inhalts-Hash er zuletzt abgeglichen wurde. Die Identitaet wird dabei
    /// festgeschrieben - ab jetzt gilt sie, nicht mehr die neu berechnete.
    /// </summary>
    private static void StampAll<T>(
        AccountApply apply,
        SyncEntityKind kind,
        Dictionary<string, T> byIdentity,
        Action<T, string, SyncStamp> assign)
    {
        if (!apply.Stamps.TryGetValue(kind, out var stamps))
        {
            return;
        }

        foreach (var (identity, stamp) in stamps)
        {
            if (byIdentity.TryGetValue(identity, out var item))
            {
                assign(item, identity, stamp);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Untergeordnete Datensaetze
    // -------------------------------------------------------------------------

    private static async Task ApplyExecutablesAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        Dictionary<string, Game> gamesByIdentity,
        CancellationToken cancellationToken)
    {
        var executables = await context.GameExecutables.ToListAsync(cancellationToken);
        var gameIdentityById = gamesByIdentity.ToDictionary(pair => pair.Value.Id, pair => pair.Key);
        var byIdentity = BuildIdentityIndex(
            executables.Where(item => gameIdentityById.ContainsKey(item.GameId)),
            item => item.CloudIdentity ?? SyncIdentity.ForExecutable(gameIdentityById[item.GameId], item));

        foreach (var incoming in apply.Upserts.Executables)
        {
            if (!gamesByIdentity.TryGetValue(incoming.GameIdentity, out var game))
            {
                continue;
            }

            if (byIdentity.TryGetValue(incoming.Identity, out var existing))
            {
                existing.ExecutablePath = incoming.ExecutablePath;
                existing.ExecutableName = incoming.ExecutableName;
                existing.IsPrimary = incoming.IsPrimary && !HasOtherPrimary(byIdentity, incoming.Identity, game.Id);
                continue;
            }

            var executable = new GameExecutable
            {
                Game = game,
                ExecutablePath = incoming.ExecutablePath,
                ExecutablePathKey = incoming.ExecutablePathKey,
                ExecutableName = incoming.ExecutableName,

                // Hoechstens eine primaere EXE je Spiel - der gefilterte Unique-Index
                // in der Datenbank laesst nichts anderes zu.
                IsPrimary = incoming.IsPrimary && !HasOtherPrimary(byIdentity, incoming.Identity, game.Id),
                AddedAtUtc = incoming.AddedAtUtc
            };

            context.GameExecutables.Add(executable);
            byIdentity[incoming.Identity] = executable;
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Executable))
        {
            if (byIdentity.TryGetValue(identity, out var executable))
            {
                context.GameExecutables.Remove(executable);
                byIdentity.Remove(identity);
            }
        }

        StampAll(apply, SyncEntityKind.Executable, byIdentity, (item, identity, stamp) =>
            (item.CloudId, item.CloudIdentity, item.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));
    }

    private static bool HasOtherPrimary(
        Dictionary<string, GameExecutable> byIdentity,
        string ownIdentity,
        long gameId) =>
        byIdentity.Any(pair =>
            pair.Key != ownIdentity &&
            pair.Value.IsPrimary &&
            (pair.Value.GameId == gameId || pair.Value.Game?.Id == gameId));

    private static async Task ApplyTagsAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        Dictionary<string, Game> gamesByIdentity,
        CancellationToken cancellationToken)
    {
        var tags = await context.GameTags.ToListAsync(cancellationToken);
        var gameIdentityById = gamesByIdentity.ToDictionary(pair => pair.Value.Id, pair => pair.Key);
        var byIdentity = BuildIdentityIndex(
            tags.Where(item => gameIdentityById.ContainsKey(item.GameId)),
            item => item.CloudIdentity ?? SyncIdentity.ForTag(gameIdentityById[item.GameId], item));

        foreach (var incoming in apply.Upserts.Tags)
        {
            if (byIdentity.ContainsKey(incoming.Identity) ||
                !gamesByIdentity.TryGetValue(incoming.GameIdentity, out var game))
            {
                continue;
            }

            var tag = new GameTag { Game = game, Tag = incoming.Tag };
            context.GameTags.Add(tag);
            byIdentity[incoming.Identity] = tag;
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Tag))
        {
            if (byIdentity.TryGetValue(identity, out var tag))
            {
                context.GameTags.Remove(tag);
                byIdentity.Remove(identity);
            }
        }

        StampAll(apply, SyncEntityKind.Tag, byIdentity, (item, identity, stamp) =>
            (item.CloudId, item.CloudIdentity, item.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));
    }

    private static async Task ApplySessionsAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        Dictionary<string, Game> gamesByIdentity,
        CancellationToken cancellationToken)
    {
        var sessions = await context.GameSessions.ToListAsync(cancellationToken);
        var gameIdentityById = gamesByIdentity.ToDictionary(pair => pair.Value.Id, pair => pair.Key);

        foreach (var incoming in apply.Upserts.Sessions)
        {
            if (!gamesByIdentity.TryGetValue(incoming.GameIdentity, out var game))
            {
                continue;
            }

            var existing = sessions.FirstOrDefault(item =>
                item.CloudId is not null && item.CloudId == incoming.CloudId);

            if (existing is not null)
            {
                existing.LastSeenAtUtc = incoming.LastSeenAtUtc;
                existing.EndedAtUtc = incoming.EndedAtUtc;
                existing.DurationSeconds = incoming.DurationSeconds;
                existing.CloudIdentity = incoming.Identity;
                existing.SyncedHash = incoming.ContentHash;
                continue;
            }

            var session = new GameSession
            {
                Game = game,
                StartedAtUtc = incoming.StartedAtUtc,
                LastSeenAtUtc = incoming.LastSeenAtUtc,
                EndedAtUtc = incoming.EndedAtUtc,
                DurationSeconds = incoming.DurationSeconds,
                BootSessionId = incoming.BootSessionId,
                CloudId = incoming.CloudId,
                CloudIdentity = incoming.Identity,
                SyncedHash = incoming.ContentHash
            };

            context.GameSessions.Add(session);
            sessions.Add(session);
        }

        // Identitaet genauso berechnen wie beim Lesen, sonst passen die Stempel nicht.
        var byIdentity = new Dictionary<string, GameSession>(StringComparer.Ordinal);
        foreach (var session in sessions.Where(item => gameIdentityById.ContainsKey(item.GameId)))
        {
            var identity = session.CloudIdentity
                ?? SyncIdentity.ForSession(apply.MachineKey, gameIdentityById[session.GameId], session);
            byIdentity.TryAdd(identity, session);
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Session))
        {
            if (byIdentity.TryGetValue(identity, out var session))
            {
                context.GameSessions.Remove(session);
                sessions.Remove(session);
                byIdentity.Remove(identity);
            }
        }

        // Ohne diesen Stempel behielte eine Session nie eine gespeicherte
        // Identitaet: sie wuerde bei jedem Lauf erneut hochgeladen, und ihr
        // Loeschen erzeugte keinen Grabstein.
        StampAll(apply, SyncEntityKind.Session, byIdentity, (item, identity, stamp) =>
            (item.CloudId, item.CloudIdentity, item.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));
    }

    private static async Task ApplyArtworkAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        Dictionary<string, Game> gamesByIdentity,
        CancellationToken cancellationToken)
    {
        var artworks = await context.GameArtworks.ToListAsync(cancellationToken);

        foreach (var incoming in apply.Upserts.Artworks)
        {
            if (incoming.ImageData is not { Length: > 0 } ||
                !gamesByIdentity.TryGetValue(incoming.GameIdentity, out var game))
            {
                continue;
            }

            var existing = artworks.FirstOrDefault(item => item.GameId == game.Id);
            if (existing is not null)
            {
                existing.ContentType = incoming.ContentType;
                existing.FileExtension = incoming.FileExtension;
                existing.Sha256 = incoming.Sha256;
                existing.ImageData = incoming.ImageData;
                existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
                existing.CloudId = incoming.CloudId;
                existing.CloudIdentity = incoming.Identity;
                existing.SyncedHash = incoming.ContentHash;
                continue;
            }

            context.GameArtworks.Add(new GameArtwork
            {
                Game = game,
                ContentType = incoming.ContentType,
                FileExtension = incoming.FileExtension,
                Sha256 = incoming.Sha256,
                ImageData = incoming.ImageData,
                UpdatedAtUtc = incoming.UpdatedAtUtc,
                CloudId = incoming.CloudId,
                CloudIdentity = incoming.Identity,
                SyncedHash = incoming.ContentHash
            });
        }

        var gameIdentityById = gamesByIdentity.ToDictionary(pair => pair.Value.Id, pair => pair.Key);
        var byIdentity = new Dictionary<string, GameArtwork>(StringComparer.Ordinal);
        foreach (var item in artworks.Where(entry => gameIdentityById.ContainsKey(entry.GameId)))
        {
            byIdentity.TryAdd(item.CloudIdentity ?? SyncIdentity.ForArtwork(gameIdentityById[item.GameId]), item);
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Artwork))
        {
            if (byIdentity.TryGetValue(identity, out var artwork))
            {
                context.GameArtworks.Remove(artwork);
                byIdentity.Remove(identity);
            }
        }

        StampAll(apply, SyncEntityKind.Artwork, byIdentity, (item, identity, stamp) =>
            (item.CloudId, item.CloudIdentity, item.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));
    }

    private static async Task ApplyExclusionsAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        CancellationToken cancellationToken)
    {
        var rules = await context.TrackingExclusionRules.ToListAsync(cancellationToken);
        var byIdentity = BuildIdentityIndex(rules, rule => rule.CloudIdentity ?? SyncIdentity.ForExclusion(rule));

        foreach (var incoming in apply.Upserts.Exclusions)
        {
            if (byIdentity.ContainsKey(incoming.Identity))
            {
                continue;
            }

            var rule = new TrackingExclusionRule
            {
                Kind = incoming.Kind,
                Value = incoming.Value,
                ValueKey = incoming.ValueKey,
                AddedAtUtc = incoming.AddedAtUtc
            };

            context.TrackingExclusionRules.Add(rule);
            byIdentity[incoming.Identity] = rule;
        }

        foreach (var identity in RemovalsFor(apply, SyncEntityKind.Exclusion))
        {
            if (byIdentity.TryGetValue(identity, out var rule))
            {
                context.TrackingExclusionRules.Remove(rule);
                byIdentity.Remove(identity);
            }
        }

        StampAll(apply, SyncEntityKind.Exclusion, byIdentity, (item, identity, stamp) =>
            (item.CloudId, item.CloudIdentity, item.SyncedHash) = (stamp.CloudId, identity, stamp.ContentHash));
    }

    private async Task ApplySettingsAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.ToListAsync(cancellationToken);
        var byKey = settings.ToDictionary(item => item.Key, StringComparer.Ordinal);

        foreach (var incoming in apply.Upserts.Settings)
        {
            if (DeviceLocalSettingKeys.Contains(incoming.Key))
            {
                continue;
            }

            if (byKey.TryGetValue(incoming.Key, out var existing))
            {
                existing.Value = incoming.Value;
                existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
                existing.SyncedHash = incoming.ContentHash;
                continue;
            }

            var setting = new AppSetting
            {
                Key = incoming.Key,
                Value = incoming.Value,
                UpdatedAtUtc = incoming.UpdatedAtUtc,
                SyncedHash = incoming.ContentHash
            };

            context.AppSettings.Add(setting);
            byKey[incoming.Key] = setting;
        }

        // Hochgeladene Einstellungen ebenfalls als abgeglichen vermerken.
        if (apply.Stamps.TryGetValue(SyncEntityKind.Setting, out var stamps))
        {
            foreach (var (identity, stamp) in stamps)
            {
                var key = identity.StartsWith("set:", StringComparison.Ordinal) ? identity[4..] : identity;
                if (byKey.TryGetValue(key, out var setting))
                {
                    setting.SyncedHash = stamp.ContentHash;
                }
            }
        }
    }

    private async Task ApplyProfileAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        CancellationToken cancellationToken)
    {
        if (apply.Upserts.Profile is not { } profile)
        {
            return;
        }

        await UpsertSettingAsync(context, AppSettingKeys.ProfileDisplayName, profile.DisplayName ?? string.Empty, cancellationToken);
        await UpsertSettingAsync(context, AppSettingKeys.ProfileAccentColor, profile.AccentColor ?? string.Empty, cancellationToken);
    }

    private async Task UpsertSettingAsync(
        YFTimeTrackerDbContext context,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await context.AppSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAtUtc = clock.UtcNow;
            return;
        }

        context.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAtUtc = clock.UtcNow });
    }

    private static async Task ResolveTombstonesAsync(
        YFTimeTrackerDbContext context,
        AccountApply apply,
        CancellationToken cancellationToken)
    {
        if (apply.ResolvedTombstones.Count == 0)
        {
            return;
        }

        // Der Grabstein hat seinen Zweck erfuellt, sobald die Loeschung im Konto
        // angekommen ist. Bliebe er liegen, wuerde er bei jedem weiteren Lauf
        // erneut eine Loeschung melden.
        var identities = apply.ResolvedTombstones.Select(item => item.Identity).ToHashSet(StringComparer.Ordinal);
        var stale = await context.SyncTombstones
            .Where(item => identities.Contains(item.Identity))
            .ToListAsync(cancellationToken);

        context.SyncTombstones.RemoveRange(stale);
    }
}
