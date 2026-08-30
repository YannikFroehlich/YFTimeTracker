namespace YFTimeTracker.Core.Services;

public static class ExecutablePathNormalizer
{
    public static string NormalizePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(executablePath.Trim()));
    }

    public static string CreateKey(string executablePath)
    {
        return NormalizePath(executablePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
