using System.Runtime.InteropServices;

namespace YFTimeTracker.App.Services;

internal static class NativeErrorDialog
{
    private const uint OkButton = 0x00000000;
    private const uint ErrorIcon = 0x00000010;
    private const uint SystemModal = 0x00001000;
    private const uint SetForeground = 0x00010000;

    public static void ShowFatalError(bool duringStartup, string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var title = duringStartup
                ? "YFTimeTracker konnte nicht gestartet werden"
                : "YFTimeTracker wurde beendet";
            var message = duringStartup
                ? "Beim Start ist ein unerwarteter Fehler aufgetreten. YFTimeTracker wird beendet."
                : "Ein unerwarteter kritischer Fehler ist aufgetreten. YFTimeTracker wird beendet.";

            MessageBoxW(
                IntPtr.Zero,
                $"{message}\n\nFehlerdetails wurden protokolliert unter:\n{logDirectory}\n\nÜber Einstellungen → Diagnose & Support kann später ein Diagnosebericht erstellt werden.",
                title,
                OkButton | ErrorIcon | SystemModal | SetForeground);
        }
        catch
        {
            // A native fallback must never trigger another fatal exception.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}
