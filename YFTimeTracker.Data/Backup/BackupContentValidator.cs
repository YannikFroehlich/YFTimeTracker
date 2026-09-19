using System.Security.Cryptography;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Data.Backup;

/// <summary>
/// Prueft einen eingehenden Datenbestand auf die Invarianten, die die lokale
/// Datenbank garantiert: gueltige Fremdschluessel, genau eine primaere EXE je
/// Spiel, hoechstens eine offene Session je Spiel, unversehrte Coverbilder.
///
/// Gemeinsam genutzt von Datei-Import und Cloud-Wiederherstellung. Beide Wege
/// bringen Daten von aussen herein und muessen denselben Massstab anlegen -
/// sonst koennte ein Weg einen Datenbestand einspielen, den der andere zu Recht
/// ablehnt, und das Tracking liefe anschliessend auf inkonsistenten Daten.
/// </summary>
internal static class BackupContentValidator
{
    private const int MaximumArtworkBytes = 10 * 1024 * 1024;

    public static void Validate(
        IReadOnlyList<Game> games,
        IReadOnlyList<GameExecutable> executables,
        IReadOnlyList<GameSession> sessions,
        IReadOnlyList<GameTag>? tags,
        IReadOnlyList<GameArtwork>? artworks,
        IReadOnlyList<TrackingExclusionRule>? trackingExclusions)
    {
        var gameIds = games.Select(game => game.Id).ToHashSet();
        var externalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.Name) ||
                (game.ExternalGameId is not null && !externalIds.Add($"{game.Source}:{game.ExternalGameId}")))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige oder doppelte Spiele.");
            }
        }

        var pathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryGameIds = new HashSet<long>();
        foreach (var executable in executables)
        {
            if (!gameIds.Contains(executable.GameId) ||
                string.IsNullOrWhiteSpace(executable.ExecutablePath) ||
                string.IsNullOrWhiteSpace(executable.ExecutablePathKey) ||
                !pathKeys.Add(executable.ExecutablePathKey) ||
                (executable.IsPrimary && !primaryGameIds.Add(executable.GameId)))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige oder doppelte EXE-Zuordnungen.");
            }
        }

        if (gameIds.Any(gameId => !primaryGameIds.Contains(gameId)))
        {
            throw new YFTimeTrackerException("Mindestens einem Spiel fehlt die primäre EXE-Zuordnung.");
        }

        var openSessionGameIds = new HashSet<long>();
        foreach (var session in sessions)
        {
            if (!gameIds.Contains(session.GameId) ||
                session.LastSeenAtUtc < session.StartedAtUtc ||
                session.EndedAtUtc < session.StartedAtUtc ||
                (session.EndedAtUtc is null && !openSessionGameIds.Add(session.GameId)))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige Sessions.");
            }
        }

        foreach (var tag in tags ?? [])
        {
            if (!gameIds.Contains(tag.GameId) || string.IsNullOrWhiteSpace(tag.Tag))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige Tag-Zuordnungen.");
            }
        }

        var artworkGameIds = new HashSet<long>();
        foreach (var artwork in artworks ?? [])
        {
            if (!gameIds.Contains(artwork.GameId)
                || !artworkGameIds.Add(artwork.GameId)
                || artwork.ImageData.Length == 0
                || artwork.ImageData.Length > MaximumArtworkBytes
                || (artwork.ContentType != "image/png" && artwork.ContentType != "image/jpeg")
                || (artwork.FileExtension != ".png" && artwork.FileExtension != ".jpg")
                || !string.Equals(
                    artwork.Sha256,
                    Convert.ToHexString(SHA256.HashData(artwork.ImageData)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige Coverbilder.");
            }
        }

        var exclusionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in trackingExclusions ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Value)
                || string.IsNullOrWhiteSpace(rule.ValueKey)
                || !Enum.IsDefined(rule.Kind)
                || !exclusionKeys.Add($"{rule.Kind}:{rule.ValueKey}"))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige Erkennungsausschlüsse.");
            }
        }
    }
}
