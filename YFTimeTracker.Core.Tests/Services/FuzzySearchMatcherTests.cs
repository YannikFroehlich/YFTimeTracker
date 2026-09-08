using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class FuzzySearchMatcherTests
{
    [TestMethod]
    public void Exact_prefix_and_contains_matches_are_ranked_before_typos()
    {
        var exact = FuzzySearchMatcher.GetScore("alpha", "Alpha");
        var prefix = FuzzySearchMatcher.GetScore("alpha", "Alphabet");
        var contains = FuzzySearchMatcher.GetScore("alpha", "The Alpha Game");
        var typo = FuzzySearchMatcher.GetScore("alpah", "Alpha");

        Assert.IsNotNull(exact);
        Assert.IsNotNull(prefix);
        Assert.IsNotNull(contains);
        Assert.IsNotNull(typo);
        Assert.IsTrue(exact < prefix);
        Assert.IsTrue(prefix < contains);
        Assert.IsTrue(contains < typo);
    }

    [TestMethod]
    public void Matching_ignores_case_and_diacritics_and_rejects_unrelated_text()
    {
        Assert.IsNotNull(FuzzySearchMatcher.GetScore("spass", "100% Spaß"));
        Assert.IsNull(FuzzySearchMatcher.GetScore("alpha", "Completely different"));
    }

    [TestMethod]
    public void Matching_checks_filename_parts_for_typographical_errors()
    {
        Assert.IsNotNull(FuzzySearchMatcher.GetScore("cyberpnuk", @"C:\Games\Cyberpunk\cyberpunk.exe"));
    }
}
