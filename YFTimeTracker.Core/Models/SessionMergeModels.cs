namespace YFTimeTracker.Core.Models;

/// <summary>Eine Session, die nach dem Zusammenführen mit diesem Zeitraum beim Zielspiel liegt.</summary>
public sealed record MergedSession(long SessionId, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc);

/// <summary>
/// Schreibplan für das Zusammenführen: <paramref name="Updates"/> werden auf das Zielspiel gesetzt und
/// auf ihren Zeitraum aktualisiert, <paramref name="RemovedSessionIds"/> sind darin aufgegangen.
/// </summary>
public sealed record SessionMergePlan(
    IReadOnlyList<MergedSession> Updates,
    IReadOnlyList<long> RemovedSessionIds);

/// <summary>Ergebnis eines Zusammenführens, für die Rückmeldung an den Benutzer.</summary>
public sealed record GameMergeResult(
    long TargetGameId,
    string TargetGameName,
    int MovedSessionCount,
    int CombinedSessionCount,
    int MovedExecutableCount);
