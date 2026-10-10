namespace YFTimeTracker.Core.Abstractions;

/// <summary>
/// Liest die Spielzeit, die der Steam-Client lokal zu einem Spiel gespeichert hat.
/// </summary>
public interface ISteamPlaytimeReader
{
    /// <summary>
    /// Gesamtspielzeit in Minuten laut Steam, oder <c>null</c>, wenn Steam fehlt
    /// oder zu dieser AppId nichts gespeichert hat.
    /// </summary>
    Task<int?> GetPlaytimeMinutesAsync(string appId, CancellationToken cancellationToken);
}
