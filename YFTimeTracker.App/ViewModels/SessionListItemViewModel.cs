using CommunityToolkit.Mvvm.ComponentModel;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class SessionListItemViewModel : ObservableObject
{
    private GameSession session;
    private DateTimeOffset nowUtc;

    public SessionListItemViewModel(
        GameSession session,
        DateTimeOffset? nowUtc = null,
        string? iconPath = null,
        string? deviceName = null)
    {
        this.session = session;
        this.nowUtc = nowUtc ?? DateTimeOffset.UtcNow;
        IconPath = iconPath;
        DeviceName = deviceName;
    }

    public long Id => session.Id;

    public long GameId => session.GameId;

    public string GameName => session.Game?.Name ?? "Unbekanntes Spiel";

    public string? IconPath { get; private set; }

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
        GameSource.BattleNet => "BATTLE.NET",
        GameSource.Ubisoft => "UBISOFT",
        GameSource.EaApp => "EA APP",
        _ => "MANUELL"
    };

    /// <summary>
    /// Geraet, auf dem die Session entstand. Bleibt <c>null</c>, solange nur
    /// dieser PC bekannt ist - dann ist die Angabe ueberfluessig.
    /// </summary>
    public string? DeviceName { get; private set; }

    public string SourceAndDeviceLabel => string.IsNullOrWhiteSpace(DeviceName)
        ? SourceLabel
        : $"{SourceLabel} · {DeviceName.ToUpperInvariant()}";

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

    public string StatusText => IsOpen ? "AKTIV" : session.IsManual ? "BEARBEITET" : "ABGESCHLOSSEN";

    public GameSession Model => session;

    /// <summary>
    /// Übernimmt einen neu geladenen Stand, damit die Liste den Eintrag behält statt ihn neu
    /// aufzubauen.
    /// </summary>
    public void Update(GameSession session, DateTimeOffset currentUtc, string? iconPath, string? deviceName)
    {
        this.session = session;
        nowUtc = currentUtc;
        IconPath = iconPath;
        DeviceName = deviceName;
        OnPropertyChanged(string.Empty);
    }

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
