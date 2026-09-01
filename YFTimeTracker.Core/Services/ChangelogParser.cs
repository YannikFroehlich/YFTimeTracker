namespace YFTimeTracker.Core.Services;

public sealed record ChangelogEntry(string Heading, IReadOnlyList<string> Bullets);

public static class ChangelogParser
{
    public static ChangelogEntry? TryGetLatestEntry(string changelogMarkdown)
    {
        if (string.IsNullOrWhiteSpace(changelogMarkdown))
        {
            return null;
        }

        string? heading = null;
        var bullets = new List<string>();

        foreach (var rawLine in changelogMarkdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    break;
                }

                heading = line[3..].Trim();
                continue;
            }

            if (heading is null)
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                bullets.Add(line[2..].Trim());
            }
        }

        return heading is null ? null : new ChangelogEntry(heading, bullets);
    }
}
