using System.Runtime.InteropServices;
using WinRT.Interop;
using YFTimeTracker.App.ViewModels;
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
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const int GwlpWndProc = -4;
    private const uint MfString = 0x00000000;
    private const uint MfGray = 0x00000001;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const int OpenCommand = 1001;
    private const int PauseCommand = 1002;
    private const int UpdateCommand = 1003;
    private const int ExitCommand = 1004;

    private readonly IGameTrackingService trackingService;
    private readonly IAppUpdateService updateService;
    private readonly WndProcDelegate wndProcDelegate;
    private MainWindow? mainWindow;
    private IntPtr hwnd;
    private IntPtr originalWndProc;
    private readonly Dictionary<TrayIconKind, (IntPtr Handle, bool Owned)> icons = new();
    private TrackingState state = TrackingState.Stopped;
    private AppUpdateState updateState;
    private bool iconAdded;

    public TrayService(
        IGameTrackingService trackingService,
        IAppUpdateService updateService)
    {
        this.trackingService = trackingService;
        this.updateService = updateService;
        updateState = updateService.State;
        wndProcDelegate = WndProc;
    }

    public void Initialize(MainWindow window)
    {
        mainWindow = window;
        hwnd = WindowNative.GetWindowHandle(window);
        originalWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(wndProcDelegate));
        icons[TrayIconKind.Active] = LoadTrayIcon("YFTimeTracker.ico");
        icons[TrayIconKind.Paused] = LoadTrayIcon("YFTimeTracker-Paused.ico");
        icons[TrayIconKind.Running] = LoadTrayIcon("YFTimeTracker-Running.ico");

        trackingService.StateChanged += TrackingService_StateChanged;
        updateService.StateChanged += UpdateService_StateChanged;

        state = trackingService.State;
        updateState = updateService.State;
        UpdateIcon(NimAdd);
        iconAdded = true;
    }

    public void Dispose()
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
        updateService.StateChanged -= UpdateService_StateChanged;

        if (iconAdded)
        {
            UpdateIcon(NimDelete);
            iconAdded = false;
        }

        if (hwnd != IntPtr.Zero && originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(hwnd, GwlpWndProc, originalWndProc);
        }

        foreach (var (handle, owned) in icons.Values)
        {
            if (owned && handle != IntPtr.Zero)
            {
                DestroyIcon(handle);
            }
        }

        icons.Clear();
    }

    private static (IntPtr Handle, bool Owned) LoadTrayIcon(string fileName)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        var handle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LrLoadFromFile);
        if (handle != IntPtr.Zero)
        {
            return (handle, true);
        }

        return (LoadIcon(IntPtr.Zero, new IntPtr(32512)), false);
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
            var updateMenu = CreateUpdateMenuPresentation(updateState);
            AppendMenu(menu, MfString, OpenCommand, "Öffnen");
            AppendMenu(menu, MfString, PauseCommand, state.IsPaused ? "Tracking fortsetzen" : "Tracking pausieren");
            AppendMenu(menu, MfString | MfGray, 0, CreateActiveGameText());
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString | (updateMenu.IsEnabled ? 0 : MfGray), UpdateCommand, updateMenu.Text);
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString, ExitCommand, "Beenden");

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmNonotify, point.X, point.Y, hwnd, IntPtr.Zero);
            _ = command switch
            {
                OpenCommand => OpenAsync(),
                PauseCommand => ToggleTrackingAsync(),
                UpdateCommand => HandleUpdateAsync(),
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

    private async Task HandleUpdateAsync()
    {
        if (mainWindow is null || !updateService.State.CanCheckForUpdates && !updateService.State.HasAvailableUpdate)
        {
            return;
        }

        if (updateService.State.HasAvailableUpdate)
        {
            await mainWindow.PromptForAvailableUpdateAsync();
            return;
        }

        await mainWindow.CheckForUpdatesManuallyAsync();
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
            hIcon = icons[SelectIconKind(state)].Handle,
            szTip = TrimForTray(CreateTrayToolTip()),
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        Shell_NotifyIcon(message, ref data);
    }

    internal static TrayIconKind SelectIconKind(TrackingState state)
    {
        if (state.IsPaused)
        {
            return TrayIconKind.Paused;
        }

        return state.RunningGames.Count == 0 ? TrayIconKind.Active : TrayIconKind.Running;
    }

    private string CreateActiveGameText()
    {
        if (state.IsPaused)
        {
            return "YFTimeTracker - Tracking pausiert";
        }

        return state.RunningGames.Count == 0
            ? "YFTimeTracker - kein aktives Spiel"
            : "YFTimeTracker - " + string.Join(", ", state.RunningGames.Select(
                game => $"{game.Name} ({TimeFormatter.Format(game.Duration)})"));
    }

    private string CreateTrayToolTip()
    {
        return updateState.HasAvailableUpdate
            ? $"YFTimeTracker - Version {updateState.AvailableVersion ?? "neu"} verfügbar"
            : CreateActiveGameText();
    }

    internal static TrayUpdateMenuPresentation CreateUpdateMenuPresentation(AppUpdateState state)
    {
        return state.Stage switch
        {
            AppUpdateStage.Checking => new TrayUpdateMenuPresentation("Suche nach Updates …", false),
            AppUpdateStage.Downloading => new TrayUpdateMenuPresentation($"Update wird geladen · {state.DownloadProgress} %", false),
            AppUpdateStage.Applying => new TrayUpdateMenuPresentation("Update wird installiert …", false),
            AppUpdateStage.Available => new TrayUpdateMenuPresentation(
                $"Neue Version {state.AvailableVersion ?? "verfügbar"} verfügbar",
                true),
            AppUpdateStage.ReadyToInstall => new TrayUpdateMenuPresentation(
                $"Neue Version {state.AvailableVersion ?? "verfügbar"} installieren",
                true),
            AppUpdateStage.Failed => new TrayUpdateMenuPresentation("Update fehlgeschlagen – erneut versuchen", true),
            _ => new TrayUpdateMenuPresentation("Nach Updates suchen", state.CanCheckForUpdates)
        };
    }

    private void TrackingService_StateChanged(object? sender, TrackingState newState)
    {
        state = newState;
        UpdateIcon(NimModify);
    }

    private void UpdateService_StateChanged(object? sender, AppUpdateState newState)
    {
        updateState = newState;
        UpdateIcon(NimModify);
    }

    public void ShowBalloonNotification(string title, string message)
    {
        if (hwnd == IntPtr.Zero || !iconAdded)
        {
            return;
        }

        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = hwnd,
            uID = IconId,
            uFlags = NifInfo,
            szTip = string.Empty,
            szInfoTitle = TrimForBalloon(title, 63),
            szInfo = TrimForBalloon(message, 255),
            dwInfoFlags = NiifInfo
        };

        Shell_NotifyIcon(NimModify, ref data);
    }

    private static string TrimForTray(string text)
    {
        return text.Length > 127 ? text[..124] + "..." : text;
    }

    private static string TrimForBalloon(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..(maxLength - 3)] + "..." : text;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr hInstance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

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

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

internal sealed record TrayUpdateMenuPresentation(string Text, bool IsEnabled);

internal enum TrayIconKind
{
    Active,
    Paused,
    Running
}
