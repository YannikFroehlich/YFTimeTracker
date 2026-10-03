using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.App.ViewModels;

public sealed class GameListItemViewModel : ObservableObject
{
    private const string ProgressNormalColor = "#FF6A2B";
    private const string ProgressLimitReachedColor = "#FF5368";
    private Game game;
    private IReadOnlyList<GameSession> gameSessions = [];
    private DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

    public GameListItemViewModel(Game game, string? iconPath = null)
    {
        this.game = game;
        IconPath = iconPath;
    }

    public GameListItemViewModel(
        Game game,
        IReadOnlyList<GameSession>? sessions,
        bool isRunning,
        DateTimeOffset nowUtc,
        string? iconPath = null)
        : this(game, iconPath)
    {
        gameSessions = sessions ?? [];
        this.nowUtc = nowUtc;
        IsRunning = isRunning;
    }

    public long Id => game.Id;

    public string Name => game.Name;

    public string? IconPath { get; private set; }

    public string ExecutablePath => game.PrimaryExecutable?.ExecutablePath ?? string.Empty;

    public string ExecutableName => game.PrimaryExecutable?.ExecutableName ?? "Keine EXE";

    public string SourceLabel => game.Source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "XBOX",
        GameSource.BattleNet => "BATTLE.NET",
        GameSource.Ubisoft => "UBISOFT",
        GameSource.EaApp => "EA APP",
        _ => "MANUELL"
    };

    public GameSource Source => game.Source;

    public string ExecutableSummary => game.Executables.Count == 1
        ? ExecutableName
        : $"{ExecutableName} + {game.Executables.Count - 1} weitere";

    public string ExecutableDisplay => Exists ? ExecutableSummary : $"{ExecutableSummary} · EXE fehlt";

    public string ExecutableColor => Exists ? "#9D9A91" : "#FF7B70";

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

    public bool IsRunning { get; private set; }

    public bool IsPinned
    {
        get => game.IsPinned;
        internal set
        {
            if (game.IsPinned == value)
            {
                return;
            }

            game.IsPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinGlyph));
            OnPropertyChanged(nameof(PinTooltip));
        }
    }

    public string PinGlyph => IsPinned ? ((char)0xE735).ToString() : ((char)0xE734).ToString();

    public string PinTooltip => IsPinned ? "Nicht mehr anheften" : "Oben anheften";

    public IReadOnlyList<string> Tags => game.Tags.Select(tag => tag.Tag).ToArray();

    // Basis-Spielzeit zaehlt in der Gesamtspielzeit mit, nicht in den
    // Zeitraum-Auswertungen - dort fehlt ihr das Datum.
    public TimeSpan TotalDuration =>
        TimeSpan.FromTicks(gameSessions.Sum(session => session.GetEffectiveDuration(nowUtc).Ticks))
        + TimeSpan.FromMinutes(game.BaselinePlaytimeMinutes ?? 0);

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

    public string ActivityColor => IsRunning ? "#6FD49A" : TotalDuration > TimeSpan.Zero ? "#FF6A2B" : "#9D9A91";

    public Visibility DailyProgressVisibility => game.DailyPlaytimeLimitMinutes is > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string DailyProgressText => game.DailyPlaytimeLimitMinutes is { } limit && limit > 0
        ? $"{GetTodayDuration().TotalMinutes:0} / {limit} Min heute"
        : string.Empty;

    public double DailyProgressPercent => game.DailyPlaytimeLimitMinutes is { } limit && limit > 0
        ? Math.Clamp(GetTodayDuration().TotalMinutes / limit * 100, 0, 100)
        : 0;

    public string DailyProgressColor => game.DailyPlaytimeLimitMinutes is { } limit
        && limit > 0
        && GetTodayDuration().TotalMinutes >= limit
            ? ProgressLimitReachedColor
            : ProgressNormalColor;

    private TimeSpan GetTodayDuration()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.Local).Date);
        return SessionOverlapCalculator.GetDurationForLocalRange(gameSessions, today, today.AddDays(1), TimeZoneInfo.Local, nowUtc);
    }

    public string SearchableText => string.Join(
        ' ',
        new[] { game.Name, game.InstallDirectory ?? string.Empty }
            .Concat(game.Executables.Select(executable => $"{executable.ExecutableName} {executable.ExecutablePath}"))
            .Concat(Tags));

    public Game Model => game;

    /// <summary>
    /// Übernimmt einen neu geladenen Stand in diesen Eintrag, statt ihn durch ein neues Objekt
    /// zu ersetzen. So bleibt der Eintrag in der Liste ausgewählt, wenn im Hintergrund neu
    /// geladen wird.
    /// </summary>
    public void Update(
        Game game,
        IReadOnlyList<GameSession>? sessions,
        bool isRunning,
        DateTimeOffset nowUtc,
        string? iconPath)
    {
        this.game = game;
        gameSessions = sessions ?? [];
        this.nowUtc = nowUtc;
        IsRunning = isRunning;
        IconPath = iconPath;
        OnPropertyChanged(string.Empty);
    }
}
