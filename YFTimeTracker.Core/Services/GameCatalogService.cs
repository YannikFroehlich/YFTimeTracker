using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Core.Services;

public sealed class GameCatalogService(
    IGameRepository games,
    IGameSessionRepository sessions,
    IClock clock) : IGameCatalogService
{
    public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken)
    {
        return games.GetAllAsync(cancellationToken);
    }

    public async Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeUserExecutablePath(executablePath);

        var key = ExecutablePathNormalizer.CreateKey(normalizedPath);
        if (await games.GetByExecutablePathKeyAsync(key, cancellationToken) is not null)
        {
            throw new YFTimeTrackerException("Diese EXE ist bereits als Spiel hinterlegt.");
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(normalizedPath)
            : displayName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new YFTimeTrackerException("Bitte gib einen Anzeigenamen an.");
        }

        return await games.AddAsync(new Game
        {
            Name = name,
            Source = GameSource.Manual,
            AddedAtUtc = clock.UtcNow,
            Executables =
            [
                new GameExecutable
                {
                    ExecutablePath = normalizedPath,
                    ExecutablePathKey = key,
                    ExecutableName = Path.GetFileName(normalizedPath),
                    IsPrimary = true,
                    AddedAtUtc = clock.UtcNow
                }
            ]
        }, cancellationToken);
    }

    public async Task UpdateGameAsync(
        long gameId,
        string displayName,
        string executablePath,
        int? dailyPlaytimeLimitMinutes,
        int? weeklyPlaytimeLimitMinutes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new YFTimeTrackerException("Bitte gib einen Anzeigenamen an.");
        }

        var game = await games.GetByIdAsync(gameId, cancellationToken)
            ?? throw new YFTimeTrackerException("Das Spiel wurde nicht gefunden.");

        var normalizedPath = NormalizeUserExecutablePath(executablePath);

        var key = ExecutablePathNormalizer.CreateKey(normalizedPath);
        var duplicate = await games.GetByExecutablePathKeyAsync(key, cancellationToken);
        if (duplicate is not null && duplicate.Id != gameId)
        {
            throw new YFTimeTrackerException("Diese EXE ist bereits einem anderen Spiel zugeordnet.");
        }

        game.Name = displayName.Trim();
        game.DailyPlaytimeLimitMinutes = dailyPlaytimeLimitMinutes is > 0 ? dailyPlaytimeLimitMinutes : null;
        game.WeeklyPlaytimeLimitMinutes = weeklyPlaytimeLimitMinutes is > 0 ? weeklyPlaytimeLimitMinutes : null;
        await games.UpdateAsync(game, cancellationToken);
        await games.SetPrimaryExecutableAsync(game.Id, new GameExecutable
        {
            ExecutablePath = normalizedPath,
            ExecutablePathKey = key,
            ExecutableName = Path.GetFileName(normalizedPath),
            IsPrimary = true,
            AddedAtUtc = clock.UtcNow
        }, cancellationToken);
    }

    public async Task<GameMergeResult> MergeGamesAsync(long sourceGameId, long targetGameId, CancellationToken cancellationToken)
    {
        if (sourceGameId == targetGameId)
        {
            throw new YFTimeTrackerException("Ein Spiel kann nicht mit sich selbst zusammengeführt werden.");
        }

        var source = await games.GetByIdAsync(sourceGameId, cancellationToken)
            ?? throw new YFTimeTrackerException("Das zusammenzuführende Spiel wurde nicht gefunden.");
        var target = await games.GetByIdAsync(targetGameId, cancellationToken)
            ?? throw new YFTimeTrackerException("Das Zielspiel wurde nicht gefunden.");

        var sourceSessions = await sessions.GetSessionsForGameAsync(sourceGameId, cancellationToken);
        var targetSessions = await sessions.GetSessionsForGameAsync(targetGameId, cancellationToken);

        // Eine laufende Session hätte zwei Probleme: das Tracking hält den alten Spieleintrag noch im
        // Speicher, und pro Spiel darf nur eine Session offen sein. Gleiche Regel wie beim Bearbeiten.
        if (sourceSessions.Any(session => session.IsOpen) || targetSessions.Any(session => session.IsOpen))
        {
            throw new YFTimeTrackerException(
                "Solange eine Session läuft, lassen sich die Spiele nicht zusammenführen. Pausiere zuerst das Tracking oder beende das Spiel.");
        }

        var plan = SessionMergePlanner.Create(targetSessions, sourceSessions);
        await games.MergeIntoAsync(sourceGameId, targetGameId, plan, cancellationToken);

        return new GameMergeResult(
            target.Id,
            target.Name,
            sourceSessions.Count,
            plan.RemovedSessionIds.Count,
            source.Executables.Count);
    }

    public Task DeleteGameAsync(long gameId, CancellationToken cancellationToken)
    {
        return games.DeleteAsync(gameId, cancellationToken);
    }

    // Hier laufen die Nutzereingaben aus Bibliothek und Spieldetails zusammen. Die Framework-
    // Wächter (ArgumentException) würden englische Meldungen samt Parameternamen in die
    // Statuszeile schreiben, deshalb wird jede Eingabe vorher auf Deutsch geprüft.
    private static string NormalizeUserExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new YFTimeTrackerException("Bitte wähle eine .exe-Datei aus.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = ExecutablePathNormalizer.NormalizePath(executablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new YFTimeTrackerException("Der Pfad zur EXE-Datei ist ungültig.", exception);
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new YFTimeTrackerException("Bitte wähle eine .exe-Datei aus.");
        }

        return normalizedPath;
    }
}
