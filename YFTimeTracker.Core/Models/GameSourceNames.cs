namespace YFTimeTracker.Core.Models;

public static class GameSourceNames
{
    /// <summary>
    /// Kurzes Kennzeichen in Großbuchstaben für Karten und Listen, etwa "STEAM".
    /// </summary>
    public static string Label(GameSource? source) => source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "XBOX",
        GameSource.BattleNet => "BATTLE.NET",
        GameSource.Ubisoft => "UBISOFT",
        GameSource.EaApp => "EA APP",
        GameSource.Riot => "RIOT",
        GameSource.Rockstar => "ROCKSTAR",
        GameSource.Amazon => "AMAZON",
        GameSource.Itch => "ITCH.IO",
        GameSource.Humble => "HUMBLE",
        _ => "MANUELL"
    };

    /// <summary>
    /// Ausgeschriebener Launcher-Name, etwa "Epic Games".
    /// </summary>
    public static string Name(GameSource source) => source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "Xbox / Microsoft Store",
        GameSource.BattleNet => "Battle.net",
        GameSource.Ubisoft => "Ubisoft Connect",
        GameSource.EaApp => "EA app",
        GameSource.Riot => "Riot Client",
        GameSource.Rockstar => "Rockstar Games Launcher",
        GameSource.Amazon => "Amazon Games",
        GameSource.Itch => "itch.io",
        GameSource.Humble => "Humble App",
        _ => "Manuell"
    };
}
