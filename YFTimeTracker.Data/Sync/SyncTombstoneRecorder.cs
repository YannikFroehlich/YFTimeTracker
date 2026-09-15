using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Sync;

/// <summary>
/// Haelt fest, dass ein Datensatz hier geloescht wurde.
///
/// Ohne diesen Nachweis waere beim naechsten Abgleich nicht zu unterscheiden, ob
/// ein Datensatz hier geloescht oder auf einem anderen PC neu angelegt wurde -
/// und jedes geloeschte Spiel kaeme aus dem Konto zurueck.
///
/// Aufgezeichnet wird nur, was ueberhaupt schon einmal im Konto stand. Ein Spiel,
/// das angelegt und vor dem ersten Abgleich wieder geloescht wurde, hat dort nie
/// existiert und braucht keinen Grabstein.
///
/// Der Loeschzeitpunkt kommt bewusst aus der Systemuhr statt aus <c>IClock</c>:
/// er ist reine Information fuer die Fehlersuche und geht in keine Entscheidung
/// des Abgleichs ein. Dafuer vier Repository-Konstruktoren umzubauen waere mehr
/// Aenderung als Nutzen.
/// </summary>
internal static class SyncTombstoneRecorder
{
    public static async Task RecordGameAsync(
        YFTimeTrackerDbContext context,
        Game game,
        CancellationToken cancellationToken)
    {
        // Das Loeschen eines Spiels raeumt per Fremdschluessel auch EXE-Dateien,
        // Tags, Sessions und Cover ab. Die brauchen eigene Grabsteine, sonst
        // blieben sie im Konto als Waisen stehen.
        var executables = await context.GameExecutables
            .Where(item => item.GameId == game.Id).ToListAsync(cancellationToken);
        var tags = await context.GameTags
            .Where(item => item.GameId == game.Id).ToListAsync(cancellationToken);
        var sessions = await context.GameSessions
            .Where(item => item.GameId == game.Id).ToListAsync(cancellationToken);
        var artwork = await context.GameArtworks
            .FirstOrDefaultAsync(item => item.GameId == game.Id, cancellationToken);

        Record(context, SyncEntityKind.Game, game.CloudIdentity, game.CloudId);

        foreach (var item in executables)
        {
            Record(context, SyncEntityKind.Executable, item.CloudIdentity, item.CloudId);
        }

        foreach (var item in tags)
        {
            Record(context, SyncEntityKind.Tag, item.CloudIdentity, item.CloudId);
        }

        foreach (var item in sessions)
        {
            Record(context, SyncEntityKind.Session, item.CloudIdentity, item.CloudId);
        }

        if (artwork is not null)
        {
            Record(context, SyncEntityKind.Artwork, artwork.CloudIdentity, artwork.CloudId);
        }
    }

    public static void RecordSession(YFTimeTrackerDbContext context, GameSession session) =>
        Record(context, SyncEntityKind.Session, session.CloudIdentity, session.CloudId);

    public static void RecordTag(YFTimeTrackerDbContext context, GameTag tag) =>
        Record(context, SyncEntityKind.Tag, tag.CloudIdentity, tag.CloudId);

    public static void RecordArtwork(YFTimeTrackerDbContext context, GameArtwork artwork) =>
        Record(context, SyncEntityKind.Artwork, artwork.CloudIdentity, artwork.CloudId);

    public static void RecordExclusion(YFTimeTrackerDbContext context, TrackingExclusionRule rule) =>
        Record(context, SyncEntityKind.Exclusion, rule.CloudIdentity, rule.CloudId);

    public static void RecordExecutable(YFTimeTrackerDbContext context, GameExecutable executable) =>
        Record(context, SyncEntityKind.Executable, executable.CloudIdentity, executable.CloudId);

    private static void Record(
        YFTimeTrackerDbContext context,
        SyncEntityKind kind,
        string? identity,
        string? cloudId)
    {
        if (string.IsNullOrEmpty(identity))
        {
            // Nie abgeglichen - im Konto gibt es nichts zu loeschen.
            return;
        }

        // Derselbe Datensatz kann in einem Vorgang mehrfach anfallen (etwa beim
        // Zusammenfuehren zweier Spiele). Der Unique-Index auf (Kind, Identity)
        // wuerde das sonst quittieren.
        var alreadyTracked = context.ChangeTracker.Entries<SyncTombstone>()
            .Any(entry => entry.Entity.Kind == kind && entry.Entity.Identity == identity);
        if (alreadyTracked)
        {
            return;
        }

        context.SyncTombstones.Add(new SyncTombstone
        {
            Kind = kind,
            Identity = identity,
            CloudId = cloudId,
            DeletedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
