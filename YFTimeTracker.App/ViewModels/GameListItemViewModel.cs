using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class GameListItemViewModel(Game game)
{
    private readonly IReadOnlyList<GameSession> gameSessions = [];
    private readonly DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

    public GameListItemViewModel(
        Game game,
        IReadOnlyList<GameSession>? sessions,
        bool isRunning,
        DateTimeOffset nowUtc)
        : this(game)
    {
        gameSessions = sessions ?? [];
        this.nowUtc = nowUtc;
        IsRunning = isRunning;
    }

    public long Id => game.Id;

    public string Name => game.Name;

    public string ExecutablePath => game.PrimaryExecutable?.ExecutablePath ?? string.Empty;

    public string ExecutableName => game.PrimaryExecutable?.ExecutableName ?? "Keine EXE";

    public string SourceLabel => game.Source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        _ => "MANUELL"
    };

    public GameSource Source => game.Source;

    public string ExecutableSummary => game.Executables.Count == 1
        ? ExecutableName
        : $"{ExecutableName} + {game.Executables.Count - 1} weitere";

    public string ExecutableDisplay => Exists ? ExecutableSummary : $"{ExecutableSummary} · EXE fehlt";

    public string ExecutableColor => Exists ? "#9AA8BF" : "#FF6B7A";

    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Length == 0
                ? "?"
                : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public bool Exists => game.Executables.Any(executable => File.Exists(executable.ExecutablePath));

    public string PathStatus => Exists ? "EXE gefunden" : "EXE fehlt oder wurde verschoben";

    public bool IsRunning { get; }

    public TimeSpan TotalDuration => TimeSpan.FromTicks(gameSessions.Sum(session => session.GetEffectiveDuration(nowUtc).Ticks));

    public string TotalPlaytime => TimeFormatter.Format(TotalDuration);

    public int SessionCount => gameSessions.Count;

    public DateTimeOffset? LastPlayedAtUtc => gameSessions.Count == 0
        ? null
        : gameSessions.Max(session => session.EndedAtUtc ?? (session.IsOpen ? nowUtc : session.LastSeenAtUtc));

    public string LastPlayedText => IsRunning
        ? "Jetzt aktiv"
        : LastPlayedAtUtc is { } lastPlayed
            ? $"Zuletzt {TimeZoneInfo.ConvertTime(lastPlayed, TimeZoneInfo.Local):dd.MM.yyyy}"
            : "Noch nicht gespielt";

    public string ActivityText => IsRunning ? "AKTIV" : TotalDuration > TimeSpan.Zero ? TotalPlaytime : "NEU";

    public string ActivityColor => IsRunning ? "#29E7A4" : TotalDuration > TimeSpan.Zero ? "#3182FF" : "#8391A8";

    public string SearchableText => string.Join(
        ' ',
        new[] { game.Name, game.InstallDirectory ?? string.Empty }
            .Concat(game.Executables.Select(executable => $"{executable.ExecutableName} {executable.ExecutablePath}")));

    public Game Model => game;
}
