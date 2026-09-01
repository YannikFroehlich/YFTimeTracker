# Entwicklung und Releases

Vielen Dank für Änderungen an YFTimeTracker. Dieses Dokument beschreibt den verbindlichen Entwicklungs-, Prüf- und Release-Ablauf.

## Voraussetzungen

- Windows 11 x64 ab Build 22621
- .NET 10 SDK
- PowerShell
- Git

Abhängigkeiten werden zentral in `Directory.Packages.props` verwaltet. Neue Paketversionen gehören deshalb nicht direkt in einzelne Projektdateien.

## Lokale Einrichtung

```powershell
git switch develop
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
dotnet run --project YFTimeTracker.App\YFTimeTracker.App.csproj
```

Vor einer Änderung sollte `git status` geprüft werden. Bereits vorhandene, nicht zugehörige Änderungen dürfen nicht überschrieben oder verworfen werden.

## Branch-Modell

- `develop` ist der Arbeitsbranch für Funktionen und Fehlerbehebungen.
- Fertige Änderungen werden per Pull Request von `develop` nach `main` gemergt.
- `main` repräsentiert den veröffentlichten Stand.
- Direkte Pushes nach `main` sollen vermieden werden.

Ein Push nach `main` startet die automatische Versionierungs- und Release-Pipeline. Ein Merge nach `main` darf daher erst erfolgen, wenn Build, Tests und erforderliche manuelle Prüfungen erfolgreich waren.

## Commit- und PR-Titel

YFTimeTracker verwendet Conventional Commits. Die höchste relevante Änderung seit dem letzten Tag bestimmt die nächste semantische Version. Bei einem Squash-Merge ist insbesondere der PR-Titel als neue Commit-Nachricht relevant.

| Nachricht | Versionsänderung | Beispiel |
| --- | --- | --- |
| `fix:` | Patch | `fix: doppelte Sessions verhindern` |
| `feat:` | Minor | `feat: Session-Editor hinzufügen` |
| `feat!:` | Major | `feat!: Exportformat neu strukturieren` |
| `BREAKING CHANGE:` im Commit-Text | Major | inkompatible Schnittstellenänderung |

Andere Commit-Typen führen standardmäßig zu einem Patch-Release. `[skip release]` in der Head-Commit-Nachricht überspringt die Veröffentlichung vollständig.

Commit-Nachrichten und PR-Titel sollten kurz, konkret und vorzugsweise auf Englisch formuliert sein, zum Beispiel `fix: align session editor inputs`.

## Architekturregeln

- `Core` bleibt unabhängig von WinUI, SQLite und konkreten Windows-APIs.
- `Data` implementiert Persistenz und Datenmigrationen, aber keine UI-Logik.
- `Windows` kapselt Windows-spezifische Prozesse, Registry-, Pfad- und Launcher-Zugriffe.
- `App` enthält Darstellung, Navigation, Tray, Updates und UI-nahe Dienste.
- Persistierte Datenformate müssen rückwärtskompatibel bleiben oder durch eine getestete Migration angehoben werden.
- Vor einer Datenbankmigration muss der bestehende automatische Backup-Ablauf erhalten bleiben.
- Neue UI-Texte sind deutsch. Nicht implementierte Funktionen müssen eindeutig als **Vorschau** oder **Demnächst** gekennzeichnet werden.
- Das Tracking verwendet ausschließlich lokale Daten. Zugangsdaten oder GitHub-Tokens dürfen niemals in der App eingebettet werden.
- Die Release-Pakete bleiben unsigniert; Code-Signierung ist kein Bestandteil des Projekts.

## Tests und manuelle Abnahme

Die vollständige automatisierte Prüfung lautet:

```powershell
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
```

GitHub Actions führt dieselben Schritte in Release-Konfiguration bei jedem Push nach `develop` sowie bei Pull Requests gegen `develop` oder `main` aus. Die CI-Prüfung muss erfolgreich sein, ersetzt aber nicht die erforderlichen manuellen Szenarien.

Je nach Änderung sind zusätzlich manuell zu prüfen:

- breite und schmale App-Fenster bei UI-Änderungen
- leere Datenbank sowie vorhandene Spiele und Sessions
- Tracking-Start, Sessionende, Pause und Tray-Betrieb
- Launcher-Erkennung mit Steam, Epic, GOG oder einem Xbox-/Microsoft-Store-Spiel
- Backup, Import und Export bei Datenänderungen
- installierte Ausgabe und Update-Ablauf bei Release- oder Updateänderungen
- Einzelinstanz, Autostart und minimierter Start bei Lifecycle-Änderungen
- Erststart mit leerer sowie Upgrade mit vorhandener Datenbank bei Änderungen am Einrichtungs-Assistenten

Der ausführliche Tracking-Ablauf steht in [docs/TRACKING_SMOKE_TEST.md](docs/TRACKING_SMOKE_TEST.md).

## Pull Requests

Ein Pull Request sollte:

1. einen Conventional-Commit-konformen Titel besitzen,
2. Zweck und sichtbare Auswirkungen beschreiben,
3. die ausgeführten Tests nennen,
4. bei UI-Änderungen Screenshots enthalten,
5. Migrationen, Exportformat- oder Einstellungsänderungen ausdrücklich erwähnen,
6. Dokumentation aktualisieren, falls Verhalten oder Bedienung geändert wurden,
7. bei sichtbaren Änderungen einen Abschnitt in `CHANGELOG.md` ergänzen, sofern noch keiner für die geplante Version existiert.

Der App zeigt beim ersten Start nach einem Update einmalig den obersten Abschnitt aus `CHANGELOG.md` an. Ohne einen neuen Abschnitt für die jeweilige Version bleibt dieser Dialog aus.

## Automatische Veröffentlichung

Nach einem Merge nach `main`:

1. `.github/workflows/auto-tag.yml` ermittelt die nächste Version.
2. `.github/workflows/release.yml` baut und testet den exakten Merge-Commit.
3. `scripts/New-Release.ps1` erstellt die selbstenthaltende portable Ausgabe.
4. Velopack erzeugt Setup, MSI sowie vollständige und – wenn möglich – Delta-Updatepakete.
5. GitHub erhält den Tag `vX.Y.Z` und ein öffentliches Release mit allen Artefakten.

Die Versionsstufe kann im Actions-Bereich über **Auto Tag YFTimeTracker** auch manuell gestartet werden. Release-Tags und veröffentlichte Artefakte dürfen nicht nachträglich auf einen anderen Commit verschoben werden.

## Lokaler Release-Test

```powershell
.\scripts\New-Release.ps1 -Version 0.5.0
```

Die angegebene Version dient nur dem lokalen Paket. Die produktive Versionsnummer wird von der GitHub-Pipeline aus den Commit-Nachrichten bestimmt.
