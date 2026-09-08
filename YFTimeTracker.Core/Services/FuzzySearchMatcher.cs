using System.Globalization;
using System.Text;

namespace YFTimeTracker.Core.Services;

public static class FuzzySearchMatcher
{
    public static int? GetScore(string searchText, string? candidate)
    {
        var query = Normalize(searchText);
        var target = Normalize(candidate);
        if (query.Length < 2 || target.Length == 0)
        {
            return null;
        }

        if (string.Equals(query, target, StringComparison.Ordinal))
        {
            return 0;
        }

        if (target.StartsWith(query, StringComparison.Ordinal))
        {
            return 10 + Math.Min(target.Length - query.Length, 9);
        }

        var containsIndex = target.IndexOf(query, StringComparison.Ordinal);
        if (containsIndex >= 0)
        {
            return 20 + Math.Min(containsIndex, 9);
        }

        if (query.Length < 3)
        {
            return null;
        }

        var maximumDistance = query.Length switch
        {
            <= 4 => 1,
            <= 7 => 2,
            _ => 3
        };

        var bestDistance = GetDistanceWithinLimit(query, target, maximumDistance);
        foreach (var part in SplitCandidate(target))
        {
            var partDistance = GetDistanceWithinLimit(query, part, maximumDistance);
            if (partDistance is not null && (bestDistance is null || partDistance < bestDistance))
            {
                bestDistance = partDistance;
            }
        }

        return bestDistance is { } distance
            ? 100 + (distance * 10) + Math.Min(Math.Abs(target.Length - query.Length), 9)
            : null;
    }

    private static int? GetDistanceWithinLimit(string left, string right, int maximumDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maximumDistance)
        {
            return null;
        }

        var previousPrevious = new int[right.Length + 1];
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
            previousPrevious[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + substitutionCost);

                if (row > 1
                    && column > 1
                    && left[row - 1] == right[column - 2]
                    && left[row - 2] == right[column - 1])
                {
                    current[column] = Math.Min(current[column], previousPrevious[column - 2] + 1);
                }

                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
            {
                return null;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[right.Length] <= maximumDistance ? previous[right.Length] : null;
    }

    private static IEnumerable<string> SplitCandidate(string candidate)
    {
        return candidate.Split(
            [' ', '\\', '/', '.', '_', '-', ':', '(', ')', '[', ']'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
