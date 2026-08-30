using System.Runtime.InteropServices;
using WinRT.Interop;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Services;

public sealed class TrayService : ITrayService
{
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8001;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const int GwlpWndProc = -4;
    private const uint MfString = 0x00000000;
    private const uint MfGray = 0x00000001;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const int OpenCommand = 1001;
    private const int PauseCommand = 1002;
    private const int ExitCommand = 1003;

    private readonly IGameTrackingService trackingService;
    private readonly WndProcDelegate wndProcDelegate;
    private MainWindow? mainWindow;
    private IntPtr hwnd;
    private IntPtr originalWndProc;
    private IntPtr iconHandle;
    private TrackingState state = TrackingState.Stopped;
    private bool iconAdded;

    public TrayService(IGameTrackingService trackingService)
    {
        this.trackingService = trackingService;
        wndProcDelegate = WndProc;
    }

    public void Initialize(MainWindow window)
    {
        mainWindow = window;
        hwnd = WindowNative.GetWindowHandle(window);
        originalWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(wndProcDelegate));
        iconHandle = LoadIcon(IntPtr.Zero, new IntPtr(32512));

        trackingService.StateChanged += (_, newState) =>
        {
            state = newState;
            UpdateIcon(NimModify);
        };

        state = trackingService.State;
        UpdateIcon(NimAdd);
        iconAdded = true;
    }

    public void Dispose()
    {
        if (iconAdded)
        {
            UpdateIcon(NimDelete);
            iconAdded = false;
        }

        if (hwnd != IntPtr.Zero && originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(hwnd, GwlpWndProc, originalWndProc);
        }
    }

    private IntPtr WndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == WmLButtonDoubleClick)
            {
                mainWindow?.ShowDashboard();
                return IntPtr.Zero;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(originalWndProc, windowHandle, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, OpenCommand, "Oeffnen");
            AppendMenu(menu, MfString, PauseCommand, state.IsPaused ? "Tracking fortsetzen" : "Tracking pausieren");
            AppendMenu(menu, MfString | MfGray, 0, CreateActiveGameText());
            AppendMenu(menu, MfString, ExitCommand, "Beenden");

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmNonotify, point.X, point.Y, hwnd, IntPtr.Zero);
            _ = command switch
            {
                OpenCommand => OpenAsync(),
                PauseCommand => ToggleTrackingAsync(),
                ExitCommand => ExitAsync(),
                _ => Task.CompletedTask
            };
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private Task OpenAsync()
    {
        mainWindow?.ShowDashboard();
        return Task.CompletedTask;
    }

    private async Task ToggleTrackingAsync()
    {
        if (trackingService.State.IsPaused)
        {
            await trackingService.ResumeAsync(CancellationToken.None);
        }
        else
        {
            await trackingService.PauseAsync(CancellationToken.None);
        }
    }

    private async Task ExitAsync()
    {
        await App.ShutdownAsync();
    }

    private void UpdateIcon(uint message)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = hwnd,
            uID = IconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = iconHandle,
            szTip = TrimForTray(CreateActiveGameText())
        };

        Shell_NotifyIcon(message, ref data);
    }

    private string CreateActiveGameText()
    {
        if (state.IsPaused)
        {
            return "YFTimeTracker - Tracking pausiert";
        }

        return state.RunningGames.Count == 0
            ? "YFTimeTracker - kein aktives Spiel"
            : "YFTimeTracker - " + string.Join(", ", state.RunningGames.Select(game => game.Name));
    }

    private static string TrimForTray(string text)
    {
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
