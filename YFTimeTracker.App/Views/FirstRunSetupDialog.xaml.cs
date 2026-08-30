using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.App.Views;

public sealed partial class FirstRunSetupDialog : ContentDialog
{
    private readonly IFirstRunSetupService setupService;
    private readonly Border[] stepIndicators;
    private int currentStep;

    public FirstRunSetupDialog(
        IFirstRunSetupService setupService,
        FirstRunSetupOptions options)
    {
        this.setupService = setupService;
        InitializeComponent();
        stepIndicators = [StepIndicator0, StepIndicator1, StepIndicator2, StepIndicator3];

        TrackingToggle.IsOn = options.TrackingEnabled;
        LauncherToggle.IsOn = options.LauncherDiscoveryEnabled;
        TrayToggle.IsOn = options.MinimizeOnClose;
        StartupToggle.IsOn = options.StartWithWindows;
        StartupToggle.IsEnabled = options.CanConfigureStartup;
        if (!options.CanConfigureStartup)
        {
            StartupHelpText.Text = options.CurrentStartupState == StartupState.DisabledByPolicy
                ? "Der Windows-Autostart ist durch eine Systemrichtlinie deaktiviert."
                : "Der Windows-Autostart ist in dieser Ausgabe nicht verfügbar.";
        }

        UpdateStep();
    }

    public bool WasCompleted { get; private set; }

    public FirstRunSetupOptions SelectedOptions => new(
        TrackingToggle.IsOn,
        LauncherToggle.IsOn,
        TrayToggle.IsOn,
        StartupToggle.IsOn,
        StartupToggle.IsEnabled ? StartupState.Disabled : StartupState.Unavailable);

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentStep == 0)
        {
            return;
        }

        currentStep--;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateStep();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentStep < 3)
        {
            currentStep++;
            ErrorText.Visibility = Visibility.Collapsed;
            UpdateStep();
            return;
        }

        BackButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        NextButton.Content = "Wird gespeichert …";
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            await setupService.CompleteAsync(SelectedOptions, CancellationToken.None);
            WasCompleted = true;
            Hide();
        }
        catch
        {
            ErrorText.Visibility = Visibility.Visible;
            BackButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
            NextButton.IsEnabled = true;
            NextButton.Content = "Einrichtung abschließen";
        }
    }

    private void UpdateStep()
    {
        WelcomeStep.Visibility = currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackingStep.Visibility = currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        WindowsStep.Visibility = currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReadyStep.Visibility = currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        for (var index = 0; index < stepIndicators.Length; index++)
        {
            stepIndicators[index].Width = index == currentStep ? 28 : 18;
            stepIndicators[index].Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                index <= currentStep ? "YFBlueBrush" : "YFStrokeBrush"];
        }

        BackButton.Visibility = currentStep == 0 ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = currentStep == 3 ? "Einrichtung abschließen" : "Weiter";
        StepText.Text = $"Schritt {currentStep + 1} von 4";

        if (currentStep == 3)
        {
            TrackingSummaryText.Text = TrackingToggle.IsOn
                ? "✓ Automatisches Tracking ist aktiv"
                : "– Automatisches Tracking startet pausiert";
            LauncherSummaryText.Text = LauncherToggle.IsOn
                ? "✓ Steam, Epic, GOG und Xbox werden lokal erkannt"
                : "– Es werden nur manuell hinterlegte Spiele erkannt";
            TraySummaryText.Text = TrayToggle.IsOn
                ? "✓ Beim Schließen läuft YFTimeTracker im Tray weiter"
                : "– Beim Schließen wird YFTimeTracker beendet";
            StartupSummaryText.Text = StartupToggle.IsEnabled && StartupToggle.IsOn
                ? "✓ YFTimeTracker startet minimiert mit Windows"
                : "– Windows-Autostart ist deaktiviert";
        }
    }
}
