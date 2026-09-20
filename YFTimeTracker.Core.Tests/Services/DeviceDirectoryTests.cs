using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class DeviceDirectoryTests
{
    [TestMethod]
    public void A_session_without_sync_identity_belongs_to_this_pc()
    {
        var session = new GameSession { GameId = 1, BootSessionId = "boot" };

        Assert.AreEqual("local", DeviceDirectory.MachineKeyOf(session, "local"));
    }

    [TestMethod]
    public void The_machine_key_comes_from_the_sync_identity_of_a_foreign_session()
    {
        var session = new GameSession
        {
            GameId = 1,
            BootSessionId = "boot",
            // Identitaet wie SyncIdentity.ForSession sie baut: der Spielteil enthaelt
            // selbst Doppelpunkte und darf die Erkennung nicht stoeren.
            CloudIdentity = "ses:other-pc:game:steam:440:638000000000000000"
        };

        Assert.AreEqual("other-pc", DeviceDirectory.MachineKeyOf(session, "local"));
    }

    [TestMethod]
    public void An_identity_of_another_record_type_falls_back_to_this_pc()
    {
        Assert.AreEqual("local", DeviceDirectory.MachineKeyOf("game:steam:440", "local"));
    }

    [TestMethod]
    public void Device_names_survive_a_round_trip_and_unknown_keys_stay_nameable()
    {
        var json = DeviceDirectory.Serialize(new Dictionary<string, string>
        {
            ["local"] = "Arbeits-PC",
            ["other-pc"] = "Wohnzimmer"
        });
        var parsed = DeviceDirectory.Parse(json);

        Assert.AreEqual("Arbeits-PC", DeviceDirectory.ResolveName("local", parsed, "local", "Arbeits-PC"));
        Assert.AreEqual("Wohnzimmer", DeviceDirectory.ResolveName("other-pc", parsed, "local", "Arbeits-PC"));
        Assert.AreEqual("Anderes Gerät", DeviceDirectory.ResolveName("unbekannt", parsed, "local", "Arbeits-PC"));
    }

    [TestMethod]
    public void A_broken_device_list_does_not_throw()
    {
        Assert.IsEmpty(DeviceDirectory.Parse("{kaputt"));
        Assert.IsEmpty(DeviceDirectory.Parse(null));
    }
}
