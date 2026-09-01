using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class ChangelogParserTests
{
    [TestMethod]
    public void TryGetLatestEntry_returns_only_the_first_section()
    {
        const string markdown = """
            # Changelog

            ## 0.12.0 – 2026-09-01
            - Erster Punkt
            - Zweiter Punkt

            ## 0.11.0 – 2026-08-20
            - Älterer Punkt
            """;

        var entry = ChangelogParser.TryGetLatestEntry(markdown);

        Assert.IsNotNull(entry);
        Assert.AreEqual("0.12.0 – 2026-09-01", entry.Heading);
        CollectionAssert.AreEqual(new[] { "Erster Punkt", "Zweiter Punkt" }, entry.Bullets.ToArray());
    }

    [TestMethod]
    public void TryGetLatestEntry_ignores_blank_lines_and_non_bullet_text()
    {
        const string markdown = """
            ## 0.12.0

            Einleitender Satz ohne Bulletpoint.

            - Wird erfasst
            * Wird auch erfasst

            """;

        var entry = ChangelogParser.TryGetLatestEntry(markdown);

        Assert.IsNotNull(entry);
        CollectionAssert.AreEqual(new[] { "Wird erfasst", "Wird auch erfasst" }, entry.Bullets.ToArray());
    }

    [TestMethod]
    public void TryGetLatestEntry_returns_empty_bullets_when_section_has_none()
    {
        const string markdown = """
            ## 0.12.0
            ## 0.11.0
            - Älterer Punkt
            """;

        var entry = ChangelogParser.TryGetLatestEntry(markdown);

        Assert.IsNotNull(entry);
        Assert.AreEqual("0.12.0", entry.Heading);
        Assert.IsEmpty(entry.Bullets);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Kein Heading, nur Text.")]
    public void TryGetLatestEntry_returns_null_when_no_heading_exists(string markdown)
    {
        Assert.IsNull(ChangelogParser.TryGetLatestEntry(markdown));
    }
}
