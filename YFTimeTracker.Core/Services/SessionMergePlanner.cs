using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Berechnet, wie die Sessions zweier Spiele zu einem Bestand zusammengelegt werden.
/// </summary>
/// <remarks>
/// Zwei Sessions desselben Spiels dürfen sich nicht überschneiden: <see cref="SessionOverlapCalculator"/>
/// summiert Sessions ohne Überschneidungen abzuziehen, gleichzeitige Sessions würden die Spielzeit also
/// doppelt zählen. Beim Zusammenführen entstehen solche Paare aber genau dann, wenn beide Einträge
/// dasselbe Spiel gleichzeitig erfasst haben – also im häufigsten Anlass für ein Zusammenführen.
/// Überschneidende Sessions werden deshalb zu einer zusammengefasst: Das Spiel lief von der frühesten
/// bis zur spätesten beobachteten Zeit, das ist die korrekte Darstellung und nie mehr Zeit als vorher.
/// </remarks>
public static class SessionMergePlanner
{
    /// <summary>
    /// Erwartet abgeschlossene Sessions; offene werden vom Aufrufer vorher abgelehnt.
    /// </summary>
    public static SessionMergePlan Create(
        IEnumerable<GameSession> targetSessions,
        IEnumerable<GameSession> sourceSessions)
    {
        ArgumentNullException.ThrowIfNull(targetSessions);
        ArgumentNullException.ThrowIfNull(sourceSessions);

        var moving = sourceSessions.Select(session => (Session: session, IsMoving: true));
        var staying = targetSessions.Select(session => (Session: session, IsMoving: false));
        var ordered = moving
            .Concat(staying)
            .OrderBy(entry => entry.Session.StartedAtUtc)
            .ThenBy(entry => entry.Session.Id)
            .ToArray();

        var updates = new List<MergedSession>();
        var removed = new List<long>();
        var index = 0;

        while (index < ordered.Length)
        {
            var survivor = ordered[index];
            var start = survivor.Session.StartedAtUtc;
            var end = EndOf(survivor.Session);
            var absorbed = new List<long>();
            var next = index + 1;

            // Nur echte Überschneidungen zusammenfassen. Aneinandergrenzende Sessions (Ende == Start)
            // gelten wie in IGameSessionRepository.HasOverlapAsync nicht als Überschneidung.
            while (next < ordered.Length && ordered[next].Session.StartedAtUtc < end)
            {
                var absorbedEnd = EndOf(ordered[next].Session);
                if (absorbedEnd > end)
                {
                    end = absorbedEnd;
                }

                absorbed.Add(ordered[next].Session.Id);
                next++;
            }

            var rangeChanged = end != EndOf(survivor.Session);
            if (survivor.IsMoving || rangeChanged)
            {
                updates.Add(new MergedSession(survivor.Session.Id, start, end));
            }

            removed.AddRange(absorbed);
            index = next;
        }

        return new SessionMergePlan(updates, removed);
    }

    private static DateTimeOffset EndOf(GameSession session)
    {
        return session.EndedAtUtc ?? session.StartedAtUtc;
    }
}
