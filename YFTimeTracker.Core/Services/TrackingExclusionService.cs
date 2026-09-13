using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Core.Services;

public sealed class TrackingExclusionService(
    ITrackingExclusionRepository repository,
    IClock clock) : ITrackingExclusionService
{
    public Task<IReadOnlyList<TrackingExclusionRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        return repository.GetAllAsync(cancellationToken);
    }

    public async Task<TrackingExclusionRule> AddAsync(
        TrackingExclusionKind kind,
        string path,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path, kind);
        var key = CreateKey(normalizedPath, kind);
        var existing = (await repository.GetAllAsync(cancellationToken))
            .FirstOrDefault(rule => rule.Kind == kind
                && string.Equals(rule.ValueKey, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        return await repository.AddAsync(new TrackingExclusionRule
        {
            Kind = kind,
            Value = normalizedPath,
            ValueKey = key,
            AddedAtUtc = clock.UtcNow
        }, cancellationToken);
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        return repository.DeleteAsync(id, cancellationToken);
    }

    public TrackingExclusionRule? FindMatch(
        RunningProcessInfo process,
        IReadOnlyList<TrackingExclusionRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Kind == TrackingExclusionKind.Executable
                && string.Equals(process.ExecutablePathKey, rule.ValueKey, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }

            if (rule.Kind == TrackingExclusionKind.Directory
                && IsPathInside(process.ExecutablePathKey, rule.ValueKey))
            {
                return rule;
            }
        }

        return null;
    }

    private static string NormalizePath(string? path, TrackingExclusionKind kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new YFTimeTrackerException(kind == TrackingExclusionKind.Executable
                ? "Bitte wähle eine .exe-Datei aus."
                : "Bitte wähle einen Ordner aus.");
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new YFTimeTrackerException("Der Ausschlusspfad ist ungültig.", exception);
        }

        if (kind == TrackingExclusionKind.Executable
            && !string.Equals(Path.GetExtension(normalized), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new YFTimeTrackerException("Bitte wähle eine .exe-Datei aus.");
        }

        return normalized;
    }

    private static string CreateKey(string path, TrackingExclusionKind kind)
    {
        return kind == TrackingExclusionKind.Executable
            ? ExecutablePathNormalizer.CreateKey(path)
            : path.ToUpperInvariant();
    }

    private static bool IsPathInside(string executablePathKey, string directoryPathKey)
    {
        if (string.IsNullOrWhiteSpace(executablePathKey) || string.IsNullOrWhiteSpace(directoryPathKey))
        {
            return false;
        }

        var directory = directoryPathKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(executablePathKey, directory, StringComparison.OrdinalIgnoreCase)
            || executablePathKey.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
