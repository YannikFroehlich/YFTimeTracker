using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class TrackingExclusionServiceTests
{
    [TestMethod]
    public async Task AddAsync_normalizes_and_deduplicates_executable_rules()
    {
        var repository = new InMemoryTrackingExclusionRepository();
        var service = new TrackingExclusionService(
            repository,
            new FakeClock(DateTimeOffset.Parse("2026-09-13T10:00:00Z")));

        var first = await service.AddAsync(
            TrackingExclusionKind.Executable,
            @"C:\Games\Tools\helper.exe",
            CancellationToken.None);
        var duplicate = await service.AddAsync(
            TrackingExclusionKind.Executable,
            @"c:\games\tools\HELPER.exe",
            CancellationToken.None);

        Assert.AreEqual(first.Id, duplicate.Id);
        Assert.HasCount(1, await service.GetRulesAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task FindMatch_respects_directory_boundaries()
    {
        var repository = new InMemoryTrackingExclusionRepository();
        var service = new TrackingExclusionService(
            repository,
            new FakeClock(DateTimeOffset.Parse("2026-09-13T10:00:00Z")));
        var rule = await service.AddAsync(
            TrackingExclusionKind.Directory,
            @"C:\Games\Ignored",
            CancellationToken.None);

        var inside = CreateProcess(@"C:\Games\Ignored\bin\game.exe");
        var sibling = CreateProcess(@"C:\Games\Ignored-Other\game.exe");

        Assert.AreEqual(rule.Id, service.FindMatch(inside, [rule])?.Id);
        Assert.IsNull(service.FindMatch(sibling, [rule]));
    }

    private static RunningProcessInfo CreateProcess(string path) => new(
        path,
        ExecutablePathNormalizer.CreateKey(path));
}
