# YFTimeTracker

YFTimeTracker ist eine lokale Windows-11-App zum automatischen Erfassen von Spielzeit. Manuell registrierte `.exe`-Dateien werden direkt erkannt; zusätzlich liest die App lokale Steam-, Epic- und GOG-Installationen und nimmt ein Launcher-Spiel beim ersten tatsächlichen Start in die Bibliothek auf. Daten, Logs, Backups und Exporte bleiben lokal unter `%LocalAppData%\YFTimeTracker`.

Das Tracking läuft im Tray weiter, wenn das Hauptfenster geschlossen wird. Der optionale Windows-Autostart startet die unpackaged App minimiert; er bleibt standardmäßig deaktiviert. Unbekannte Prozesse außerhalb erkannter Spieleordner werden nicht als Spiele behandelt.

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

Das ZIP ist nicht digital signiert. Ein signiertes MSIX benoetigt zusaetzlich ein vertrauenswuerdiges Code-Signing-Zertifikat; ohne ein solches Zertifikat ist die portable Ausgabe die direkt nutzbare Release-Variante.

## Branches und automatische Releases

Neue Funktionen werden auf `develop` entwickelt und anschliessend per Pull Request nach `main` gemergt. Jeder Merge nach `main` startet die automatische Semantic-Versioning- und Release-Pipeline. Die Commit- beziehungsweise Squash-Merge-Nachricht bestimmt die Versionsstufe:

- `fix:` erzeugt einen Patch-Release.
- `feat:` erzeugt einen Minor-Release.
- `feat!:` oder `BREAKING CHANGE:` erzeugt einen Major-Release.
- `[skip release]` ueberspringt die Veroeffentlichung.

Das GitHub Release enthaelt einen Velopack-Installer, ein MSI, Updatepakete, das portable ZIP, dessen SHA-256-Pruefsumme und ein Release-Manifest. Die vollstaendigen Regeln stehen in [CONTRIBUTING.md](CONTRIBUTING.md).
