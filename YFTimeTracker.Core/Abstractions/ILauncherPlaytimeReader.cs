using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

/// <summary>
/// Liest die Spielzeit, die ein Launcher lokal zu einem Spiel gespeichert hat.
/// </summary>
public interface ILauncherPlaytimeReader
{
    /// <summary>
    /// Launcher, dessen Spielzeit dieser Leser kennt.
    /// </summary>
    GameSource Source { get; }

    /// <summary>
    /// Gesamtspielzeit in Minuten laut Launcher, oder <c>null</c>, wenn der Launcher fehlt
    /// oder zu dieser Launcher-Id nichts gespeichert hat.
    /// </summary>
    Task<int?> GetPlaytimeMinutesAsync(string externalGameId, CancellationToken cancellationToken);
}
