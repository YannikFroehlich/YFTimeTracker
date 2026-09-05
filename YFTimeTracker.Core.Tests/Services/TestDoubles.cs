using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Tests.Services;

internal sealed class FakeClock(DateTimeOffset nowUtc) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = nowUtc;
}

internal sealed class FakeBootSessionProvider(string bootSessionId) : IBootSessionProvider
{
    public string BootSessionId { get; set; } = bootSessionId;

    public string GetCurrentBootSessionId() => BootSessionId;
}

internal sealed class FakeProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlySet<string> RunningPathKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RunningProcessInfo>? RunningProcesses { get; set; }

    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref callCount);
        return Task.FromResult(RunningProcesses ?? RunningPathKeys
            .Select(path => new RunningProcessInfo(path, path))
            .ToArray());
    }
}

internal sealed class FakeGameInstallationProvider : IGameInstallationProvider
{
    public LauncherDiscoveryResult Result { get; set; } = LauncherDiscoveryResult.Empty;

    public Exception? Exception { get; set; }

    public int CallCount { get; private set; }

    public Task<LauncherDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Exception is null ? Task.FromResult(Result) : Task.FromException<LauncherDiscoveryResult>(Exception);
    }
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> settings = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings[key] = value;
        return Task.CompletedTask;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        return int.TryParse(await GetAsync(key, cancellationToken), out var value) ? value : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken)
    {
        return bool.TryParse(await GetAsync(key, cancellationToken), out var value) ? value : fallback;
    }
}

internal sealed class InMemoryGameRepository : IGameRepository
{
    private readonly List<Game> games = [];
    private long nextId = 1;
    private long nextExecutableId = 1;

    public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Game>>(games.Select(Clone).OrderBy(game => game.Name).ToArray());
    }

    public Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(games.FirstOrDefault(game => game.Id == id) is { } game ? Clone(game) : null);
    }

    public Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(games.FirstOrDefault(game => game.Executables.Any(executable => executable.ExecutablePathKey == executablePathKey)) is { } game ? Clone(game) : null);
    }

    public Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(games.FirstOrDefault(game => game.Source == source && game.ExternalGameId == externalGameId) is { } game ? Clone(game) : null);
    }

    public Task<Game> AddAsync(Game game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        game.Id = nextId++;
        foreach (var executable in game.Executables)
        {
            executable.Id = nextExecutableId++;
            executable.GameId = game.Id;
        }
        games.Add(Clone(game));
        return Task.FromResult(Clone(game));
    }

    public Task UpdateAsync(Game game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = games.FindIndex(candidate => candidate.Id == game.Id);
        if (index >= 0)
        {
            games[index] = Clone(game);
        }

        return Task.CompletedTask;
    }

    public Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var game = games.Single(candidate => candidate.Id == gameId);
        if (games.Any(candidate => candidate.Executables.Any(item => item.ExecutablePathKey == executable.ExecutablePathKey)))
        {
            throw new InvalidOperationException("Duplicate executable path.");
        }

        executable.Id = nextExecutableId++;
        executable.GameId = gameId;
        game.Executables.Add(CloneExecutable(executable));
        return Task.FromResult(CloneExecutable(executable));
    }

    public Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var game = games.Single(candidate => candidate.Id == gameId);
        foreach (var item in game.Executables)
        {
            item.IsPrimary = false;
        }

        var target = game.Executables.FirstOrDefault(item => item.ExecutablePathKey == executable.ExecutablePathKey);
        if (target is null)
        {
            executable.Id = nextExecutableId++;
            executable.GameId = gameId;
            target = CloneExecutable(executable);
            game.Executables.Add(target);
        }

        target.IsPrimary = true;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        games.RemoveAll(game => game.Id == id);
        return Task.CompletedTask;
    }

    private static Game Clone(Game game)
    {
        return new Game
        {
            Id = game.Id,
            Name = game.Name,
            Source = game.Source,
            ExternalGameId = game.ExternalGameId,
            InstallDirectory = game.InstallDirectory,
            InstallDirectoryKey = game.InstallDirectoryKey,
            AddedAtUtc = game.AddedAtUtc,
            Executables = game.Executables.Select(CloneExecutable).ToList()
        };
    }

    private static GameExecutable CloneExecutable(GameExecutable executable) => new()
    {
        Id = executable.Id,
        GameId = executable.GameId,
        ExecutablePath = executable.ExecutablePath,
        ExecutablePathKey = executable.ExecutablePathKey,
        ExecutableName = executable.ExecutableName,
        IsPrimary = executable.IsPrimary,
        AddedAtUtc = executable.AddedAtUtc
    };
}

internal sealed class InMemoryGameSessionRepository(Func<long, Game?> gameResolver) : IGameSessionRepository
{
    private readonly List<GameSession> sessions = [];
    private long nextId = 1;

    public DateTimeOffset? LastQueryFromUtc { get; private set; }

    public DateTimeOffset? LastQueryToUtc { get; private set; }

    public Task<GameSession?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(sessions.FirstOrDefault(session => session.Id == id) is { } session ? Clone(session) : null);
    }

    public Task<IReadOnlyList<GameSession>> GetOpenSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GameSession>>(sessions.Where(session => session.EndedAtUtc is null).Select(Clone).ToArray());
    }

    public Task<IReadOnlyList<GameSession>> GetSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastQueryFromUtc = fromUtc;
        LastQueryToUtc = toUtc;
        var query = sessions.AsEnumerable();
        if (fromUtc.HasValue)
        {
            query = query.Where(session => (session.EndedAtUtc ?? session.LastSeenAtUtc) > fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(session => session.StartedAtUtc < toUtc.Value);
        }

        return Task.FromResult<IReadOnlyList<GameSession>>(query.Select(Clone).ToArray());
    }

    public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GameSession>>(sessions.Where(session => session.GameId == gameId).Select(Clone).ToArray());
    }

    public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = sessions
            .Where(session => session.GameId == gameId)
            .Where(session => (session.EndedAtUtc ?? session.LastSeenAtUtc) > fromUtc)
            .Where(session => session.StartedAtUtc < toUtc);
        return Task.FromResult<IReadOnlyList<GameSession>>(query.Select(Clone).ToArray());
    }

    public Task<IReadOnlyList<GameSession>> GetRecentCompletedSessionsAsync(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GameSession>>(sessions
            .Where(session => session.EndedAtUtc is not null)
            .OrderByDescending(session => session.EndedAtUtc)
            .Take(count)
            .Select(Clone)
            .ToArray());
    }

    public Task<GameSession> AddAsync(GameSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        session.Id = nextId++;
        sessions.Add(Clone(session));
        return Task.FromResult(Clone(session));
    }

    public Task UpdateAsync(GameSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = sessions.FindIndex(candidate => candidate.Id == session.Id);
        if (index >= 0)
        {
            sessions[index] = Clone(session);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sessions.RemoveAll(session => session.Id == id);
        return Task.CompletedTask;
    }

    public Task<bool> HasOverlapAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasOverlap = sessions.Any(session =>
            session.GameId == gameId &&
            session.Id != excludedSessionId &&
            session.StartedAtUtc < endedAtUtc &&
            (session.EndedAtUtc is null || session.EndedAtUtc.Value > startedAtUtc));
        return Task.FromResult(hasOverlap);
    }

    private GameSession Clone(GameSession session)
    {
        return new GameSession
        {
            Id = session.Id,
            GameId = session.GameId,
            Game = gameResolver(session.GameId),
            StartedAtUtc = session.StartedAtUtc,
            LastSeenAtUtc = session.LastSeenAtUtc,
            EndedAtUtc = session.EndedAtUtc,
            DurationSeconds = session.DurationSeconds,
            BootSessionId = session.BootSessionId
        };
    }
}

internal sealed class InMemoryPlaytimeReadRepository(IGameSessionRepository sessions) : IPlaytimeReadRepository
{
    public async Task<PlaytimeOverview> GetOverviewAsync(
        DateTimeOffset nowUtc,
        int recentGameCount,
        CancellationToken cancellationToken)
    {
        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);
        var recentGames = allSessions
            .Where(session => session.Game is not null)
            .GroupBy(session => session.GameId)
            .Select(group => new RecentGameInfo(
                group.Key,
                group.First().Game!.Name,
                group.Max(session => session.EndedAtUtc ?? nowUtc),
                TimeSpan.FromSeconds(group.Sum(session => GetEffectiveSeconds(session, nowUtc))),
                group.Any(session => session.IsOpen)))
            .OrderByDescending(game => game.LastPlayedAtUtc)
            .ThenBy(game => game.Name)
            .Take(recentGameCount)
            .ToArray();

        return new PlaytimeOverview(
            allSessions.Sum(session => GetEffectiveSeconds(session, nowUtc)),
            allSessions.Select(session => session.GameId).Distinct().Count(),
            recentGames);
    }

    public async Task<long> GetTotalDurationSecondsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);
        return allSessions.Sum(session => GetEffectiveSeconds(session, nowUtc));
    }

    public async Task<DateTimeOffset?> GetEarliestSessionStartAsync(CancellationToken cancellationToken)
    {
        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);
        return allSessions.Count == 0 ? null : allSessions.Min(session => session.StartedAtUtc);
    }

    private static long GetEffectiveSeconds(GameSession session, DateTimeOffset nowUtc)
    {
        return session.DurationSeconds
            ?? Math.Max(0, Convert.ToInt64(Math.Floor(session.GetEffectiveDuration(nowUtc).TotalSeconds)));
    }
}
