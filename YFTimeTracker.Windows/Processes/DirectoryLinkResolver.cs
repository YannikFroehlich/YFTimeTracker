namespace YFTimeTracker.Windows.Processes;

internal interface IDirectoryLinkResolver
{
    /// <summary>
    /// Löst Verknüpfungen (Junctions, Symlinks) auf das tatsächliche Zielverzeichnis auf.
    /// Ist das Verzeichnis keine Verknüpfung, wird der Pfad unverändert zurückgegeben.
    /// </summary>
    string ResolveFinalTarget(string directory);
}

internal sealed class WindowsDirectoryLinkResolver : IDirectoryLinkResolver
{
    private const int MaximumLinkDepth = 8;

    public string ResolveFinalTarget(string directory)
    {
        var current = directory;

        // Jede Verknüpfung wird einzeln aufgelöst, weil das direkte Auflösen des Endziels den
        // Ordner selbst öffnen muss - unter %ProgramFiles%\WindowsApps schlägt das mit
        // "Zugriff verweigert" fehl, das Lesen des Verknüpfungsziels dagegen nicht.
        for (var depth = 0; depth < MaximumLinkDepth; depth++)
        {
            var target = ResolveOnce(current);
            if (target is null || string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = target;
        }

        return current;
    }

    private static string? ResolveOnce(string directory)
    {
        try
        {
            var target = new DirectoryInfo(directory).ResolveLinkTarget(returnFinalTarget: false)?.FullName;
            return target is null ? null : Path.TrimEndingDirectorySeparator(target);
        }
        catch (Exception exception) when (exception
            is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
