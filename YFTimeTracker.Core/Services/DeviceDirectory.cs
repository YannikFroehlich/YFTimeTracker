using System.Text.Json;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Ordnet Sessions dem Geraet zu, auf dem sie entstanden sind.
///
/// Eine eigene Spalte dafuer braucht es nicht: die Identitaet im Konto
/// (<see cref="SyncIdentity.ForSession"/>) beginnt mit dem MachineKey des
/// Ursprungsgeraets, und eine Session ohne Identitaet wurde noch nie
/// abgeglichen - die kann nur von diesem PC stammen.
///
/// Die Namen der Geraete kommen aus dem Konto und liegen lokal als
/// geraetegebundene Einstellung, damit sie ohne Netz verfuegbar sind.
/// </summary>
public static class DeviceDirectory
{
    public static string MachineKeyOf(string? cloudIdentity, string localMachineKey)
    {
        if (string.IsNullOrEmpty(cloudIdentity))
        {
            return localMachineKey;
        }

        var parts = cloudIdentity.Split(':');
        return parts.Length >= 3 && parts[0] == "ses" && parts[1].Length > 0
            ? parts[1]
            : localMachineKey;
    }

    public static string MachineKeyOf(GameSession session, string localMachineKey) =>
        MachineKeyOf(session.CloudIdentity, localMachineKey);

    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // Eine beschaedigte Zwischenablage der Geraetenamen darf die Sessions
            // nicht aufhalten; ohne Namen greift der Rueckfalltext.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public static string Serialize(IReadOnlyDictionary<string, string> devices) =>
        JsonSerializer.Serialize(devices);

    /// <summary>
    /// Anzeigename eines Geraets. Der eigene PC bleibt auch dann erkennbar, wenn
    /// noch nie abgeglichen wurde und deshalb kein Name aus dem Konto vorliegt.
    /// </summary>
    public static string ResolveName(
        string machineKey,
        IReadOnlyDictionary<string, string> knownDevices,
        string localMachineKey,
        string localDeviceName)
    {
        if (string.Equals(machineKey, localMachineKey, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(localDeviceName) ? "Dieser PC" : localDeviceName;
        }

        return knownDevices.TryGetValue(machineKey, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "Anderes Gerät";
    }
}
