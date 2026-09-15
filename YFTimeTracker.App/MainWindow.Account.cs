using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App;

/// <summary>
/// Das Konto im Profil-Flyout oben rechts.
///
/// Bewusst hier und nicht in den Einstellungen: das Profil ist der Ort, an dem
/// man sich in einer App anmeldet. Ohne Anmeldung laeuft alles unveraendert
/// lokal weiter - das Konto ist ein Angebot, keine Voraussetzung.
/// </summary>
public sealed partial class MainWindow
{
    private ICloudAuthService AuthService => App.Services.GetRequiredService<ICloudAuthService>();

    private IAccountSyncService SyncService => App.Services.GetRequiredService<IAccountSyncService>();

    private bool accountBusy;

    private async void AccountSignIn_Click(object sender, RoutedEventArgs e) =>
        await RunAccountAuthAsync((email, password) =>
            AuthService.SignInAsync(email, password, CancellationToken.None));

    private async void AccountSignUp_Click(object sender, RoutedEventArgs e) =>
        await RunAccountAuthAsync((email, password) =>
            AuthService.SignUpAsync(email, password, CancellationToken.None));

    private async Task RunAccountAuthAsync(Func<string, string, Task<CloudAuthResult>> authenticate)
    {
        var email = AccountEmailInput.Text.Trim();
        var password = AccountPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            ShowAccountStatus("Bitte E-Mail und Passwort eingeben.");
            return;
        }

        SetAccountBusy(true, "Anmeldung läuft …");
        try
        {
            var result = await authenticate(email, password);
            ShowAccountStatus(result.Message);

            if (result.IsSuccess)
            {
                await settingsStore.SetAsync(AppSettingKeys.CloudUserEmail, email, CancellationToken.None);
                RefreshAccountPanels();

                // Direkt nach der Anmeldung abgleichen: der Benutzer erwartet, dass
                // seine Daten jetzt im Konto stehen, nicht erst beim naechsten Start.
                await SyncAccountAsync();
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Anmeldung am Konto fehlgeschlagen");
            ShowAccountStatus($"Anmeldung fehlgeschlagen: {exception.Message}");
        }
        finally
        {
            // Das Passwort verlaesst das Eingabefeld sofort wieder, egal wie der
            // Versuch ausgegangen ist.
            AccountPasswordInput.Password = string.Empty;
            SetAccountBusy(false);
            RefreshAccountPanels();
        }
    }

    private async void AccountSync_Click(object sender, RoutedEventArgs e) => await SyncAccountAsync();

    private async Task SyncAccountAsync()
    {
        if (accountBusy)
        {
            return;
        }

        SetAccountBusy(true, "Abgleich läuft …");
        try
        {
            var summary = await SyncService.SyncNowAsync(progress: null, CancellationToken.None);
            if (summary is null)
            {
                ShowAccountStatus("Abgleich nicht möglich: keine gültige Anmeldung.");
                return;
            }

            ShowAccountStatus(summary.Conflicts.Count == 0
                ? $"Abgeglichen: {summary.Uploaded} hochgeladen, {summary.Downloaded} übernommen."
                : $"Abgeglichen: {summary.Uploaded} hoch, {summary.Downloaded} runter, "
                  + $"{summary.Conflicts.Count} Konflikt(e) – dort hat der Stand aus dem Konto gewonnen.");

            await dashboardViewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Kontoabgleich fehlgeschlagen");
            ShowAccountStatus($"Abgleich fehlgeschlagen: {exception.Message}");
        }
        finally
        {
            SetAccountBusy(false);
            await RefreshAccountLastSyncAsync();
        }
    }

    private async void AccountSignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AuthService.SignOutAsync(CancellationToken.None);
            ShowAccountStatus("Abgemeldet. Die lokalen Daten bleiben unverändert.");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Abmelden fehlgeschlagen");
        }
        finally
        {
            RefreshAccountPanels();
        }
    }

    /// <summary>Blendet je nach Anmeldestand den richtigen Teil des Flyouts ein.</summary>
    private void RefreshAccountPanels()
    {
        var session = AuthService.CurrentSession;
        var signedIn = session is not null;

        AccountSignedInPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountSignedOutPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;

        if (signedIn)
        {
            AccountEmailText.Text = string.IsNullOrWhiteSpace(session!.Email)
                ? "Angemeldet"
                : $"Angemeldet als {session.Email}";
        }

        ProfileScopeText.Text = signedIn
            ? "Name und Farbe gehören zum Konto und gelten auf allen deinen PCs."
            : "Name und Farbe werden auf diesem Gerät gespeichert.";
    }

    private async Task RefreshAccountLastSyncAsync()
    {
        if (AuthService.CurrentSession is null)
        {
            return;
        }

        var lastSync = await SyncService.GetLastSyncAtUtcAsync(CancellationToken.None);
        AccountLastSyncText.Text = lastSync is { } timestamp
            ? $"Zuletzt abgeglichen: {timestamp.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Noch nicht abgeglichen";
    }

    private void SetAccountBusy(bool busy, string? message = null)
    {
        accountBusy = busy;
        AccountSignInButton.IsEnabled = !busy;
        AccountSignUpButton.IsEnabled = !busy;
        AccountSyncButton.IsEnabled = !busy;

        if (message is not null)
        {
            ShowAccountStatus(message);
        }
    }

    private void ShowAccountStatus(string message)
    {
        AccountStatusText.Text = message;
        AccountStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
