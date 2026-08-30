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

Der Entwicklungs-Build ist als unpackaged WinUI-App konfiguriert, damit er ohne lokale WinUI-Vorlage stabil baut. `Package.appxmanifest` und die MSIX-Assets sind vorbereitet.

Ein Test mit

```powershell
dotnet publish YFTimeTracker.App\YFTimeTracker.App.csproj -c Release -r win-x64 --no-restore -p:EnableMsixTooling=true -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
```

erzeugt die Release-DLLs, scheitert in dieser lokalen Toolchain aber in `Microsoft.Windows.SDK.BuildTools.MSIX` an der fehlenden MSBuild-Task-Abhaengigkeit `System.Security.Permissions, Version=8.0.0.0`. Fuer ein installierbares MSIX-Paket ist deshalb noch eine Packaging-Toolchain-Korrektur plus ein vertrauenswuerdiges Signierzertifikat noetig.
