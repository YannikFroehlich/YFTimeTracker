using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class SessionListItemViewModel(GameSession session)
{
    public long Id => session.Id;

    public string StartedAt => session.StartedAtUtc.LocalDateTime.ToString("g");

    public string EndedAt => session.EndedAtUtc?.LocalDateTime.ToString("g") ?? "Läuft";

    public string Duration => TimeFormatter.Format(TimeSpan.FromSeconds(session.DurationSeconds ?? 0));

    public GameSession Model => session;
}
