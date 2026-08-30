using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class GameListItemViewModel(Game game)
{
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

    public string ExecutableSummary => game.Executables.Count == 1
        ? ExecutableName
        : $"{ExecutableName} + {game.Executables.Count - 1} weitere";

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

    public Game Model => game;
}
