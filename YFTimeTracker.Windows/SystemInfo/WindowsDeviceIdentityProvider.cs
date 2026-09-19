using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.SystemInfo;

/// <summary>
/// Stabile Kennung dieses Rechners fuer die Cloud-Sicherung.
///
/// Grundlage ist die MachineGuid aus der Registry - sie ueberlebt Umbenennungen
/// des Rechners und App-Neuinstallationen. Sie wird zusammen mit dem
/// Benutzernamen gehasht, damit zwei Windows-Konten auf demselben PC getrennte
/// Geraete in der Cloud bleiben und die rohe MachineGuid den Rechner nicht
/// ueber die Cloud identifizierbar macht.
/// </summary>
public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
    private const string CryptographyKeyPath = @"SOFTWARE\Microsoft\Cryptography";

    private readonly Lazy<string> machineKey = new(ComputeMachineKey, isThreadSafe: true);

    public string MachineKey => machineKey.Value;

    public string DeviceName => Environment.MachineName;

    private static string ComputeMachineKey()
    {
        var seed = ReadMachineGuid() ?? Environment.MachineName;
        var bytes = Encoding.UTF8.GetBytes($"{seed}|{Environment.UserName}");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            // 64-Bit-View erzwingen: unter WOW64 zeigt die Standardansicht auf den
            // Wow6432Node-Zweig, in dem die MachineGuid nicht liegt.
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptography = localMachine.OpenSubKey(CryptographyKeyPath);
            return cryptography?.GetValue("MachineGuid") as string;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
