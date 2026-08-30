using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.App.Views;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Data;
using YFTimeTracker.Windows;
using YFTimeTracker.Windows.SystemInfo;

namespace YFTimeTracker.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\YFTimeTracker.SingleInstance";
    private const string ActivationEventName = @"Local\YFTimeTracker.Activate";
    private IHost? host;
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;
    private RegisteredWaitHandle? activationRegistration;
    private bool isPrimaryInstance = true;
    private bool activationRequested;
    private static int shutdownStarted;
    private static int fatalErrorReported;

    public static IServiceProvider Services => ((App)Current).host?.Services
        ?? throw new InvalidOperationException("The application host has not been initialized.");

    public static MainWindow? MainWindow { get; private set; }

    public static bool IsShuttingDown => Volatile.Read(ref shutdownStarted) != 0;

    public App()
    {
        InitializeSingleInstance();
        UnhandledException += OnUnhandledException;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!isPrimaryInstance)
        {
            Exit();
            return;
        }

        var pathProvider = new WindowsAppPathProvider();
        Directory.CreateDirectory(pathProvider.LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(pathProvider.LogDirectory, "yftimetracker-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddYFTimeTrackerCore();
                services.AddYFTimeTrackerWindowsServices();
                services.AddYFTimeTrackerData();

                services.AddSingleton<IFilePickerService, WinUiFilePickerService>();
                services.AddSingleton<IStartupService, WinUiStartupService>();
                services.AddSingleton<ITrayService, TrayService>();
                services.AddSingleton<IAppUpdateService, VelopackAppUpdateService>();
                services.AddSingleton<IAppDiagnosticsService, AppDiagnosticsService>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<GamesPage>();
                services.AddTransient<GameDetailsPage>();
                services.AddTransient<SessionsPage>();
                services.AddTransient<StatisticsPage>();
                services.AddTransient<SettingsPage>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<GamesViewModel>();
                services.AddTransient<GameDetailsViewModel>();
                services.AddSingleton<SessionsViewModel>();
                services.AddSingleton<StatisticsViewModel>();
                services.AddSingleton<SettingsViewModel>();
            })
            .Build();

        await host.StartAsync();
        await Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync(CancellationToken.None);

        MainWindow = Services.GetRequiredService<MainWindow>();
        Services.GetRequiredService<ITrayService>().Initialize(MainWindow);
        MainWindow.Activate();

        await Services.GetRequiredService<IGameTrackingService>().StartAsync(CancellationToken.None);

        var startMinimized = args.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (startMinimized)
        {
            MainWindow.HideToTray();
        }
        else if (activationRequested)
        {
            MainWindow.ShowDashboard();
        }

        _ = MainWindow.CheckForUpdatesOnStartupAsync(showPrompt: !startMinimized);
    }

    public static async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0 || Current is not App app)
        {
            return;
        }

        try
        {
            if (app.host is not null)
            {
                await app.host.Services.GetRequiredService<IGameTrackingService>().StopAsync(CancellationToken.None);
                app.host.Services.GetRequiredService<ITrayService>().Dispose();
                await app.host.StopAsync(CancellationToken.None);
                app.host.Dispose();
                app.host = null;
            }
        }
        finally
        {
            app.activationRegistration?.Unregister(null);
            app.activationEvent?.Dispose();
            if (app.instanceMutex is not null)
            {
                try
                {
                    app.instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                app.instanceMutex.Dispose();
            }

            Log.CloseAndFlush();
            Current.Exit();
        }
    }

    private void InitializeSingleInstance()
    {
        try
        {
            instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
            isPrimaryInstance = createdNew;
            if (!createdNew)
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        using var existingEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                        existingEvent.Set();
                        break;
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        if (attempt < 19)
                        {
                            Thread.Sleep(100);
                        }
                    }
                }

                return;
            }

            activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
            activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) =>
                {
                    activationRequested = true;
                    MainWindow?.ShowDashboard();
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception exception)
        {
            isPrimaryInstance = true;
            Log.Warning(exception, "Single-instance coordination is unavailable.");
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception.");
        if (Interlocked.Exchange(ref fatalErrorReported, 1) != 0)
        {
            return;
        }

        e.Handled = true;
        var logDirectory = new WindowsAppPathProvider().LogDirectory;
        NativeErrorDialog.ShowFatalError(MainWindow is null, logDirectory);
        _ = ShutdownAsync();
    }
}
