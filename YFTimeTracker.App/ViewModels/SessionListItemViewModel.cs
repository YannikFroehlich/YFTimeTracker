using CommunityToolkit.Mvvm.ComponentModel;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class SessionListItemViewModel : ObservableObject
{
    private readonly GameSession session;
    private DateTimeOffset nowUtc;

    public SessionListItemViewModel(GameSession session, DateTimeOffset? nowUtc = null)
    {
        this.session = session;
        this.nowUtc = nowUtc ?? DateTimeOffset.UtcNow;
    }

    public long Id => session.Id;

    public long GameId => session.GameId;

    public string GameName => session.Game?.Name ?? "Unbekanntes Spiel";

    public string GameInitials
    {
        get
        {
            var words = GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Length == 0
                ? "?"
                : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public string SourceLabel => session.Game?.Source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "XBOX",
        _ => "MANUELL"
    };

    public DateTimeOffset StartedAtUtc => session.StartedAtUtc;

    public DateTimeOffset? EndedAtUtc => session.EndedAtUtc;

    public string StartedAt => session.StartedAtUtc.LocalDateTime.ToString("g");

    public string EndedAt => session.EndedAtUtc?.LocalDateTime.ToString("g") ?? "Läuft";

    public string DateLabel => session.StartedAtUtc.LocalDateTime.ToString("ddd, dd.MM.yyyy");

    public string TimeRange => session.EndedAtUtc is { } endedAtUtc
        ? $"{session.StartedAtUtc.LocalDateTime:HH:mm} – {endedAtUtc.LocalDateTime:HH:mm}"
        : $"Seit {session.StartedAtUtc.LocalDateTime:HH:mm}";

    public TimeSpan EffectiveDuration => session.GetEffectiveDuration(nowUtc);

    public string Duration => IsOpen
        ? TimeFormatter.FormatClock(EffectiveDuration)
        : TimeFormatter.Format(EffectiveDuration);

    public bool IsOpen => session.IsOpen;

    public bool CanModify => !IsOpen;

    public string StatusText => IsOpen ? "AKTIV" : "ABGESCHLOSSEN";

    public GameSession Model => session;

    public void RefreshDuration(DateTimeOffset currentUtc)
    {
        if (!IsOpen)
        {
            return;
        }

        nowUtc = currentUtc;
        OnPropertyChanged(nameof(EffectiveDuration));
        OnPropertyChanged(nameof(Duration));
    }
}
