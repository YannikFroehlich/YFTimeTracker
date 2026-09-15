using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class SyncPlannerTests
{
    private static LocalSyncEntry Local(string identity, string hash, string? syncedHash = null, string? cloudId = null) =>
        new(identity, 1, cloudId, hash, syncedHash);

    private static RemoteSyncEntry Remote(string identity, string hash, bool isDeleted = false) =>
        new(identity, $"cloud-{identity}", hash, DateTimeOffset.UnixEpoch, isDeleted);

    private static SyncPlan Plan(
        IReadOnlyCollection<LocalSyncEntry>? local = null,
        IReadOnlyCollection<RemoteSyncEntry>? remote = null,
        IReadOnlyCollection<LocalTombstone>? tombstones = null) =>
        SyncPlanner.Plan(SyncEntityKind.Game, local ?? [], remote ?? [], tombstones ?? []);

    [TestMethod]
    public void New_local_entry_is_uploaded()
    {
        var plan = Plan(local: [Local("game:steam:440", "hash-a")]);

        Assert.AreEqual(1, plan.Upload.Count);
        Assert.AreEqual("game:steam:440", plan.Upload[0].Identity);
        Assert.AreEqual(0, plan.ApplyLocally.Count);
    }

    [TestMethod]
    public void Entry_that_only_exists_in_the_account_is_pulled_down()
    {
        var plan = Plan(remote: [Remote("game:steam:440", "hash-a")]);

        Assert.AreEqual(1, plan.ApplyLocally.Count);
        Assert.AreEqual(0, plan.Upload.Count);
    }

    [TestMethod]
    public void Unchanged_entry_on_both_sides_causes_no_traffic()
    {
        var plan = Plan(
            local: [Local("game:steam:440", "hash-a", syncedHash: "hash-a")],
            remote: [Remote("game:steam:440", "hash-a")]);

        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void Locally_edited_entry_is_uploaded()
    {
        var plan = Plan(
            local: [Local("game:steam:440", "hash-neu", syncedHash: "hash-alt")],
            remote: [Remote("game:steam:440", "hash-alt")]);

        Assert.AreEqual(1, plan.Upload.Count);
        Assert.AreEqual(0, plan.ApplyLocally.Count);
        Assert.AreEqual(0, plan.Conflicts.Count);
    }

    [TestMethod]
    public void Entry_edited_on_the_other_pc_is_applied_here()
    {
        var plan = Plan(
            local: [Local("game:steam:440", "hash-alt", syncedHash: "hash-alt")],
            remote: [Remote("game:steam:440", "hash-neu")]);

        Assert.AreEqual(1, plan.ApplyLocally.Count);
        Assert.AreEqual(0, plan.Upload.Count);
        Assert.AreEqual(0, plan.Conflicts.Count);
    }

    [TestMethod]
    public void Edited_on_both_sides_keeps_the_account_version_and_reports_a_conflict()
    {
        var plan = Plan(
            local: [Local("game:steam:440", "hash-hier", syncedHash: "hash-alt")],
            remote: [Remote("game:steam:440", "hash-dort")]);

        Assert.AreEqual(1, plan.ApplyLocally.Count);
        Assert.AreEqual(0, plan.Upload.Count);
        Assert.AreEqual(1, plan.Conflicts.Count);
        Assert.AreEqual("game:steam:440", plan.Conflicts[0].Identity);
    }

    [TestMethod]
    public void Identical_edits_on_both_sides_are_not_a_conflict()
    {
        // Beide Geraete haben dasselbe Spiel angeheftet. Es gibt nichts zu klaeren.
        var plan = Plan(
            local: [Local("game:steam:440", "hash-gleich", syncedHash: "hash-alt")],
            remote: [Remote("game:steam:440", "hash-gleich")]);

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual(0, plan.Conflicts.Count);
    }

    [TestMethod]
    public void Locally_deleted_entry_is_tombstoned_in_the_account()
    {
        var plan = Plan(
            remote: [Remote("game:steam:440", "hash-a")],
            tombstones: [new LocalTombstone("game:steam:440", "cloud-1", DateTimeOffset.UnixEpoch)]);

        Assert.AreEqual(1, plan.TombstoneRemotely.Count);
        Assert.AreEqual(0, plan.ApplyLocally.Count);
    }

    [TestMethod]
    public void Deleting_on_one_pc_does_not_resurrect_the_entry_on_the_next_sync()
    {
        // Genau der Fehler, den Grabsteine verhindern: ohne sie waere "fehlt
        // lokal" nicht von "auf dem anderen PC neu angelegt" zu unterscheiden,
        // und das geloeschte Spiel kaeme zurueck.
        var plan = Plan(
            remote: [Remote("game:steam:440", "hash-a")],
            tombstones: [new LocalTombstone("game:steam:440", "cloud-1", DateTimeOffset.UnixEpoch)]);

        Assert.AreEqual(0, plan.ApplyLocally.Count, "Das gelöschte Spiel darf nicht zurückkommen.");
    }

    [TestMethod]
    public void Entry_deleted_in_the_account_is_removed_here()
    {
        var plan = Plan(
            local: [Local("game:steam:440", "hash-a", syncedHash: "hash-a")],
            remote: [Remote("game:steam:440", "hash-a", isDeleted: true)]);

        Assert.AreEqual(1, plan.DeleteLocally.Count);
        Assert.AreEqual(0, plan.ApplyLocally.Count);
    }

    [TestMethod]
    public void Entry_deleted_in_the_account_and_absent_here_needs_no_action()
    {
        var plan = Plan(remote: [Remote("game:steam:440", "hash-a", isDeleted: true)]);

        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void Recreating_a_deleted_entry_locally_uploads_it_again()
    {
        // Der Grabstein wird beim Neuanlegen entfernt; kommt er trotzdem noch
        // mit, darf der vorhandene lokale Datensatz nicht verschwinden.
        var plan = Plan(
            local: [Local("game:steam:440", "hash-neu")],
            tombstones: [new LocalTombstone("game:steam:440", "cloud-1", DateTimeOffset.UnixEpoch)]);

        Assert.AreEqual(1, plan.Upload.Count);
        Assert.AreEqual(0, plan.TombstoneRemotely.Count);
    }

    [TestMethod]
    public void Tombstone_for_an_entry_that_is_already_gone_from_the_account_is_dropped()
    {
        var plan = Plan(tombstones: [new LocalTombstone("game:steam:440", "cloud-1", DateTimeOffset.UnixEpoch)]);

        Assert.IsTrue(plan.IsEmpty);
    }

    [TestMethod]
    public void First_sync_of_an_existing_library_merges_instead_of_duplicating()
    {
        // Beide PCs haben dasselbe Steam-Spiel schon lokal, keiner hat je
        // synchronisiert. Es darf kein zweiter Eintrag entstehen.
        var plan = Plan(
            local: [Local("game:steam:440", "hash-hier")],
            remote: [Remote("game:steam:440", "hash-dort")]);

        Assert.AreEqual(0, plan.Upload.Count);
        Assert.AreEqual(1, plan.ApplyLocally.Count);
        Assert.AreEqual(1, plan.Conflicts.Count);
    }

    [TestMethod]
    public void Plan_covers_every_identity_exactly_once()
    {
        var plan = Plan(
            local:
            [
                Local("a", "h1"),
                Local("b", "h2", syncedHash: "h2")
            ],
            remote:
            [
                Remote("b", "h2"),
                Remote("c", "h3")
            ],
            tombstones: [new LocalTombstone("d", null, DateTimeOffset.UnixEpoch)]);

        var touched = plan.Upload.Select(entry => entry.Identity)
            .Concat(plan.ApplyLocally.Select(entry => entry.Identity))
            .Concat(plan.DeleteLocally.Select(entry => entry.Identity))
            .Concat(plan.TombstoneRemotely.Select(entry => entry.Identity))
            .ToList();

        CollectionAssert.AreEquivalent(new[] { "a", "c" }, touched);
    }
}

[TestClass]
public sealed class SyncIdentityTests
{
    [TestMethod]
    public void Launcher_games_are_identified_by_source_and_launcher_id()
    {
        var onePc = new Game { Id = 5, Name = "Half-Life", Source = GameSource.Steam, ExternalGameId = "70" };
        var otherPc = new Game { Id = 91, Name = "Half-Life", Source = GameSource.Steam, ExternalGameId = "70" };

        Assert.AreEqual(SyncIdentity.ForGame(onePc), SyncIdentity.ForGame(otherPc));
    }

    [TestMethod]
    public void The_same_game_from_two_launchers_stays_separate()
    {
        var steam = new Game { Name = "Doom", Source = GameSource.Steam, ExternalGameId = "379720" };
        var gog = new Game { Name = "Doom", Source = GameSource.Gog, ExternalGameId = "379720" };

        Assert.AreNotEqual(SyncIdentity.ForGame(steam), SyncIdentity.ForGame(gog));
    }

    [TestMethod]
    public void Manual_games_match_on_the_normalized_name()
    {
        var onePc = new Game { Name = "The Witcher 3" };
        var otherPc = new Game { Name = "the  witcher   3" };

        Assert.AreEqual(SyncIdentity.ForGame(onePc), SyncIdentity.ForGame(otherPc));
    }

    [TestMethod]
    public void Different_manual_games_keep_different_identities()
    {
        Assert.AreNotEqual(
            SyncIdentity.ForGame(new Game { Name = "Portal" }),
            SyncIdentity.ForGame(new Game { Name = "Portal 2" }));
    }

    [TestMethod]
    public void Sessions_from_two_devices_never_collide()
    {
        var session = new GameSession
        {
            GameId = 1,
            StartedAtUtc = new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero)
        };

        Assert.AreNotEqual(
            SyncIdentity.ForSession("machine-a", "game:steam:440", session),
            SyncIdentity.ForSession("machine-b", "game:steam:440", session));
    }

    [TestMethod]
    public void The_same_session_uploaded_twice_keeps_its_identity()
    {
        var session = new GameSession
        {
            GameId = 1,
            StartedAtUtc = new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero)
        };

        Assert.AreEqual(
            SyncIdentity.ForSession("machine-a", "game:steam:440", session),
            SyncIdentity.ForSession("machine-a", "game:steam:440", session));
    }

    [TestMethod]
    public void Hash_changes_when_a_synced_field_changes()
    {
        var game = new Game { Name = "Doom", Source = GameSource.Steam, ExternalGameId = "1" };
        var before = SyncIdentity.HashGame(game);

        game.IsPinned = true;

        Assert.AreNotEqual(before, SyncIdentity.HashGame(game));
    }

    [TestMethod]
    public void Hash_ignores_fields_that_are_not_synced()
    {
        // Die lokale Id und der Installationsordner sind geraetespezifisch und
        // duerfen keinen Abgleich ausloesen.
        var game = new Game { Id = 1, Name = "Doom", InstallDirectory = @"C:\Games\Doom" };
        var before = SyncIdentity.HashGame(game);

        game.Id = 99;
        game.InstallDirectory = @"D:\Spiele\Doom";

        Assert.AreEqual(before, SyncIdentity.HashGame(game));
    }

    [TestMethod]
    public void Field_boundaries_cannot_be_shifted_to_fake_an_equal_hash()
    {
        var first = new Game { Name = "ab", ExternalGameId = "c", Source = GameSource.Steam };
        var second = new Game { Name = "a", ExternalGameId = "bc", Source = GameSource.Steam };

        Assert.AreNotEqual(SyncIdentity.HashGame(first), SyncIdentity.HashGame(second));
    }

    [TestMethod]
    public void Empty_and_missing_fields_hash_differently()
    {
        var empty = new Game { Name = "Doom", ExternalGameId = string.Empty, Source = GameSource.Steam };
        var missing = new Game { Name = "Doom", ExternalGameId = null, Source = GameSource.Steam };

        Assert.AreNotEqual(SyncIdentity.HashGame(empty), SyncIdentity.HashGame(missing));
    }
}
