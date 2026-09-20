using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Geraeteunabhaengige Identitaeten und Inhalts-Hashes fuer den Kontoabgleich.
///
/// Die lokale <c>Id</c> taugt dafuer nicht: Spiel 5 auf dem einen PC ist nicht
/// Spiel 5 auf dem anderen. Gebraucht wird eine Kennung, die beide Geraete
/// unabhaengig voneinander auf denselben Wert berechnen.
/// </summary>
public static class SyncIdentity
{
    /// <summary>
    /// Launcher-Spiele werden ueber Quelle und Launcher-Id erkannt - dieselbe
    /// Steam-Id meint ueberall dasselbe Spiel. Manuell angelegte Spiele haben
    /// keine solche Kennung; dort bleibt nur der normalisierte Name. Der Pfad
    /// taugt nicht, weil er sich zwischen zwei PCs fast immer unterscheidet.
    /// </summary>
    public static string ForGame(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.ExternalGameId) && game.Source != GameSource.Manual)
        {
            return $"game:{game.Source.ToString().ToLowerInvariant()}:{game.ExternalGameId.Trim().ToLowerInvariant()}";
        }

        return $"game:manual:{NormalizeName(game.Name)}";
    }

    /// <summary>
    /// EXE-Dateien gehoeren zu einem Spiel und werden ueber ihren normalisierten
    /// Pfad unterschieden. Derselbe Pfad auf beiden PCs ist dieselbe Datei;
    /// abweichende Pfade bleiben beide erhalten, weil auf jedem Geraet ohnehin
    /// nur der dort vorhandene Pfad je greift.
    /// </summary>
    public static string ForExecutable(string gameIdentity, GameExecutable executable) =>
        $"exe:{gameIdentity}:{executable.ExecutablePathKey.Trim().ToLowerInvariant()}";

    public static string ForTag(string gameIdentity, GameTag tag) =>
        $"tag:{gameIdentity}:{tag.Tag.Trim().ToLowerInvariant()}";

    public static string ForArtwork(string gameIdentity) => $"art:{gameIdentity}";

    public static string ForExclusion(TrackingExclusionRule rule) =>
        $"excl:{rule.Kind.ToString().ToLowerInvariant()}:{rule.ValueKey.Trim().ToLowerInvariant()}";

    public static string ForSetting(string key) => $"set:{key}";

    /// <summary>
    /// Sessions entstehen auf genau einem Geraet und sind nie "dieselbe" Session
    /// wie eine auf einem anderen PC. Identitaet ist deshalb Geraet plus Spiel
    /// plus Startzeitpunkt - reproduzierbar auch dann, wenn dieselbe Session ein
    /// zweites Mal hochgeladen wird.
    /// </summary>
    public static string ForSession(string machineKey, string gameIdentity, GameSession session) =>
        $"ses:{machineKey}:{gameIdentity}:{session.StartedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Name ohne Gross-/Kleinschreibung, ohne Sonderzeichen und ohne
    /// Mehrfach-Leerzeichen. Damit trifft "The Witcher 3" auch "the witcher 3"
    /// und "The  Witcher  3".
    /// </summary>
    public static string NormalizeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasSeparator = false;

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    // -------------------------------------------------------------------------
    // Inhalts-Hashes
    //
    // Der Hash deckt genau die Felder ab, die abgeglichen werden. Aendert sich
    // keines davon, gilt der Datensatz als unveraendert - auch wenn die Zeile
    // aus anderen Gruenden neu geschrieben wurde.
    // -------------------------------------------------------------------------

    public static string HashGame(Game game) => Hash(
        game.Name,
        game.Source.ToString(),
        game.ExternalGameId,
        game.DailyPlaytimeLimitMinutes?.ToString(CultureInfo.InvariantCulture),
        game.WeeklyPlaytimeLimitMinutes?.ToString(CultureInfo.InvariantCulture),
        game.BaselinePlaytimeMinutes?.ToString(CultureInfo.InvariantCulture),
        game.IsPinned.ToString(),
        game.AddedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));

    public static string HashExecutable(GameExecutable executable) => Hash(
        executable.ExecutablePath,
        executable.ExecutablePathKey,
        executable.ExecutableName,
        executable.IsPrimary.ToString());

    public static string HashTag(GameTag tag) => Hash(tag.Tag);

    public static string HashSession(GameSession session) => Hash(
        session.StartedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
        session.LastSeenAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
        session.EndedAtUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture),
        session.DurationSeconds?.ToString(CultureInfo.InvariantCulture),
        session.BootSessionId);

    public static string HashArtwork(GameArtwork artwork) => Hash(
        artwork.ContentType,
        artwork.FileExtension,
        artwork.Sha256);

    public static string HashExclusion(TrackingExclusionRule rule) => Hash(
        rule.Kind.ToString(),
        rule.Value,
        rule.ValueKey);

    public static string HashSetting(string key, string value) => Hash(key, value);

    /// <summary>
    /// Verkettet die Felder laengenpraefigiert statt mit einem Trennzeichen.
    ///
    /// Ein Trennzeichen waere nur so lange eindeutig, wie es in keinem Feld
    /// vorkommt - bei Spielnamen und Dateipfaden laesst sich das nicht
    /// garantieren. Mit vorangestellter Laenge ergeben ("ab", "c") und
    /// ("a", "bc") zuverlaessig verschiedene Hashes, und ein nicht gesetztes
    /// Feld ist an der Laenge -1 von einem leeren unterscheidbar.
    /// </summary>
    private static string Hash(params string?[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part?.Length ?? -1).Append(':').Append(part);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..32];
    }
}
