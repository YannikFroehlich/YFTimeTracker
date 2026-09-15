using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.Security;

/// <summary>
/// Legt Zugangsdaten im Windows-Anmeldeinformationsspeicher ab.
///
/// Bewusst nicht die SQLite-Datenbank: die wird exportiert, gesichert und in
/// den Diagnosepaketen mitgeliefert. Ein Refresh-Token darf in keiner dieser
/// Dateien landen. Der Anmeldeinformationsspeicher verschluesselt den Wert an
/// das Windows-Benutzerkonto gebunden, sodass er auf einem anderen Rechner auch
/// dann nutzlos ist, wenn jemand die Datei kopiert.
/// </summary>
public sealed class CredentialManagerSecretStore(ILogger<CredentialManagerSecretStore>? logger = null) : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    /// <summary>Windows begrenzt den Blob auf 5 * 512 Byte.</summary>
    private const int MaximumBlobBytes = 2560;

    private readonly ILogger<CredentialManagerSecretStore> log =
        logger ?? NullLogger<CredentialManagerSecretStore>.Instance;

    public string? Read(string name)
    {
        if (!CredRead(name, CredentialTypeGeneric, 0, out var handle))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var buffer = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, buffer, 0, buffer.Length);
            return Encoding.Unicode.GetString(buffer);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Write(string name, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > MaximumBlobBytes)
        {
            throw new ArgumentException(
                $"Der Wert ist zu lang für den Windows-Anmeldeinformationsspeicher ({bytes.Length} von maximal {MaximumBlobBytes} Byte).",
                nameof(secret));
        }

        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);

            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = name,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Der Wert konnte nicht im Windows-Anmeldeinformationsspeicher abgelegt werden (Fehler {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string name)
    {
        // Ein nicht vorhandener Eintrag ist kein Fehler: Abmelden muss auch dann
        // durchlaufen, wenn nie ein Token gespeichert wurde.
        if (!CredDelete(name, CredentialTypeGeneric, 0))
        {
            log.LogDebug(
                "Eintrag {Name} konnte nicht entfernt werden (Fehler {Error}); vermutlich war er nicht vorhanden",
                name, Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref Credential userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
