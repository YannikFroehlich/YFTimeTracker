# YFTimeTracker

YFTimeTracker ist eine lokale Windows-11-App zum automatischen Erfassen von Spielzeit. Manuell registrierte `.exe`-Dateien werden direkt erkannt; zusätzlich liest die App lokale Steam-, Epic- und GOG-Installationen und nimmt ein Launcher-Spiel beim ersten tatsächlichen Start in die Bibliothek auf. Daten, Logs, Backups und Exporte bleiben lokal unter `%LocalAppData%\YFTimeTracker`.

Das Tracking läuft im Tray weiter, wenn das Hauptfenster geschlossen wird. Der optionale Windows-Autostart startet die unpackaged App minimiert; er bleibt standardmäßig deaktiviert. Unbekannte Prozesse außerhalb erkannter Spieleordner werden nicht als Spiele behandelt.

Mehrere Prozesse und alternative EXE-Dateien desselben Spiels werden zu genau einer laufenden Session zusammengefasst. Prozessneustarts erzeugen getrennte Sessions. Längere unbeobachtete Zeiträume werden nach Möglichkeit getrennt, die Standby-Erkennung bleibt jedoch vom jeweiligen Windows-Energiesparmodus abhängig. Nach einem App-Absturz stellt YFTimeTracker eine offene Session nur dann wieder her, wenn das Spiel im selben Windows-Start weiterhin läuft. Andernfalls endet sie am letzten gespeicherten Lebenszeichen.

Installierte Setup-/MSI-Ausgaben prüfen beim Start automatisch den stabilen GitHub-Release-Kanal. Eine manuelle Prüfung ist unter **Einstellungen → App-Updates** möglich. Gefundene Updates werden erst nach Bestätigung heruntergeladen, zeigen ihren Fortschritt in der App und werden nach einem sauberen Neustart installiert.

## Projektstruktur

- `YFTimeTracker.Core`: Domain-Modelle, Services, Tracking-Regeln, Statistik und Validierung.
- `YFTimeTracker.Data`: EF Core, SQLite, Migration, Repositories, Backup, Export und Import.
- `YFTimeTracker.Windows`: Windows-Pfade, Prozess-Snapshot, Boot-Session- und lokale Launcher-Erkennung.
- `YFTimeTracker.App`: WinUI-3-App mit Dashboard, Spieleverwaltung und Einstellungen.
- `YFTimeTracker.Core.Tests`, `YFTimeTracker.Data.Tests` und `YFTimeTracker.Windows.Tests`: Tracking-, Migrations-, Backup- und Launcher-Tests.

## Entwickeln

```powershell
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
dotnet run --project YFTimeTracker.App\YFTimeTracker.App.csproj
```

Die realen Launcher-, Tray-, Standby- und Wiederherstellungsabläufe stehen im [Tracking-Smoke-Test](docs/TRACKING_SMOKE_TEST.md).

## Packaging

Der Entwicklungs-Build bleibt als unpackaged WinUI-App konfiguriert. Ein reproduzierbares, selbstenthaltendes Windows-x64-Release wird mit folgendem Befehl erstellt:

```powershell
.\scripts\New-Release.ps1
```

Das Skript erzeugt die Logo-Assets neu, fuehrt Restore, Release-Build und alle Tests aus und legt danach diese Dateien unter `artifacts\release` ab:

- `YFTimeTracker-v0.1.0-win-x64.zip`: portable App inklusive Windows App SDK und .NET Runtime.
- `YFTimeTracker-v0.1.0-win-x64.zip.sha256`: SHA-256-Pruefsumme.

Eine abweichende Version kann uebergeben werden:

```powershell
.\scripts\New-Release.ps1 -Version 0.1.1
```

## Branches und automatische Releases

Neue Funktionen werden auf `develop` entwickelt und anschliessend per Pull Request nach `main` gemergt. Jeder Merge nach `main` startet die automatische Semantic-Versioning- und Release-Pipeline. Die Commit- beziehungsweise Squash-Merge-Nachricht bestimmt die Versionsstufe:

- `fix:` erzeugt einen Patch-Release.
- `feat:` erzeugt einen Minor-Release.
- `feat!:` oder `BREAKING CHANGE:` erzeugt einen Major-Release.
- `[skip release]` ueberspringt die Veroeffentlichung.

Das GitHub Release enthaelt einen Velopack-Installer, ein MSI, Updatepakete, das portable ZIP, dessen SHA-256-Pruefsumme und ein Release-Manifest. Die vollstaendigen Regeln stehen in [CONTRIBUTING.md](CONTRIBUTING.md).

## Automatische Updates

Die App verwendet den öffentlichen Release-Feed von `https://github.com/YannikFroehlich/YFTimeTracker`. Es wird kein GitHub-Token in den Quellcode, den Build oder die installierte Anwendung eingebettet. Vorabversionen werden nicht automatisch angeboten.

Self-Updates stehen in der mit Velopack installierten Setup-/MSI-Ausgabe zur Verfügung. Entwicklungs- und nicht installierte portable Builds zeigen ihren Versionsstatus an, verändern sich aber nicht selbst. Vor dem Neustart beendet YFTimeTracker das Tracking kontrolliert, damit offene Sessions korrekt gespeichert werden.

## Diagnose und Support

Unter **Einstellungen → Diagnose & Support** zeigt die App ihre Version, Runtime sowie Installations-, Daten- und Logordner an. Der Logordner kann direkt geöffnet und ein Diagnose-ZIP exportiert werden. Dieses enthält Systeminformationen und höchstens drei aktuelle Logdateien, aber keine Datenbank, Backups oder Spielsessions. Bei einem kritischen Startfehler zeigt YFTimeTracker unabhängig von WinUI eine verständliche Windows-Meldung mit dem Speicherort der Fehlerprotokolle.
