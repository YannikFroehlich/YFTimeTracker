# YFTimeTracker

[![CI](https://github.com/YannikFroehlich/YFTimeTracker/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/YannikFroehlich/YFTimeTracker/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/YannikFroehlich/YFTimeTracker?label=Release)](https://github.com/YannikFroehlich/YFTimeTracker/releases/latest)
[![Lizenz](https://img.shields.io/github/license/YannikFroehlich/YFTimeTracker)](LICENSE)
![Plattform](https://img.shields.io/badge/Plattform-Windows%2011%20x64-0078D6)

<p align="center">
  <img src="YFTimeTracker.App/Assets/YFTimeTrackerLogo.png" alt="YFTimeTracker-Logo" width="180">
</p>

YFTimeTracker ist eine lokale Windows-11-App zum automatischen Erfassen und Auswerten von Spielzeit. Sie erkennt manuell hinterlegte Programmdateien sowie lokale Steam-, Epic-, GOG- und Xbox-/Microsoft-Store-Installationen. Ein Launcher-Spiel wird erst beim ersten tatsächlichen Start in die Bibliothek übernommen.

Das Repository ist öffentlich. Die App arbeitet trotzdem vollständig lokal: Konten, Cloud-Dienste und Launcher-Web-APIs sind für das Tracking nicht erforderlich.

## Inhalt

- [Funktionen](#funktionen)
- [Installation](#installation)
- [Lokale Daten und Datenschutz](#lokale-daten-und-datenschutz)
- [Automatisches Tracking](#automatisches-tracking)
- [Entwicklung](#entwicklung)
- [Lokales Release-Paket](#lokales-release-paket)
- [Branches und automatische Releases](#branches-und-automatische-releases)
- [Automatische Updates](#automatische-updates)
- [Mitwirken und Support](#mitwirken-und-support)
- [Lizenz](#lizenz)

## Funktionen

**Tracking und Erkennung**

- Automatische Erkennung laufender Spiele und sekundengenaue Sessions
- Mehrere EXE-Dateien und Prozesse pro Spiel ohne doppelte Sessions
- Lokale Launcher-Erkennung für Steam, Epic Games, GOG, Xbox-/Microsoft-Store, Battle.net und Ubisoft Connect
- Xbox-Erkennung über die lokale Windows-Paketverwaltung und `MicrosoftGame.config`, ohne Xbox-Anmeldung oder Web-API
- Optionales Tages- und Wochenlimit pro Spiel mit Fortschrittsanzeige in Bibliothek, Spieldetails und Dashboard sowie einmaliger Windows-Benachrichtigung bei Erreichen

**Dashboard und Auswertung**

- Dashboard mit Live-Tracking, Tages-, Wochen- und Gesamtwerten
- Globale Suche nach Spielen, EXE-Dateien, Sessions und App-Bereichen
- Statistiken für frei wählbare Zeiträume und einzelne Spiele mit CSV-Export
- Jahresrückblick mit Monatsverlauf, Vorjahresvergleich, Rekorden, Top-Spielen und PNG-Export

**Bibliothek und Sessions**

- Bibliothek mit Suche, Sortierung, Spieldetails und EXE-Verwaltung
- Spiele direkt aus den Spieldetails heraus starten
- Lokale Spiel-Icons aus den registrierten EXE-Dateien mit datensparsamem Cache
- Anlegen, Bearbeiten und Löschen manueller Sessions, inklusive CSV-Export der Sessions-Liste
- Direkter Link zum Explorer-Ordner nach einem Export

**Windows-Integration**

- Ersteinrichtungs-Assistent für Tracking, Launcher, Tray und Autostart
- Tray-Betrieb, Tracking-Pause, optionaler Autostart und Einzelinstanz-Schutz
- Optional minimierter Start direkt im Tray, sowohl beim manuellen Start als auch über den Windows-Autostart
- Helles und dunkles Design, umschaltbar in den Einstellungen
- Lokales Profil mit editierbarem Anzeigename und Akzentfarbe (nur auf diesem Gerät gespeichert)
- Benachrichtigungsverlauf (erreichte Zeitlimits, verfügbare Updates) über das Glocken-Icon im Header

**Daten, Updates und Diagnose**

- Lokale Backups sowie Import und Export
- Automatische Update-Prüfung für installierte Ausgaben
- "Was ist neu"-Dialog mit den Änderungen der Version beim ersten Start nach einem Update
- Diagnoseansicht und Export eines datensparsamen Diagnose-ZIP

## Installation

Die aktuelle stabile Version steht unter [GitHub Releases](https://github.com/YannikFroehlich/YFTimeTracker/releases/latest) bereit.

- **Setup (`YFTimeTracker-win-Setup.exe`)**: empfohlene Installation mit automatischen Updates.
- **MSI (`YFTimeTracker-win.msi`)**: alternative Windows-Installation, ebenfalls updatefähig.
- **Portable ZIP**: vollständig entpacken und `YFTimeTracker.App.exe` starten. Portable Builds zeigen ihren Versionsstatus an, unterstützen aber keine automatischen Updates.

YFTimeTracker wird für Windows 11 x64 veröffentlicht und bringt die benötigte .NET- und Windows-App-SDK-Laufzeit mit. Die Pakete sind bewusst nicht digital signiert; Windows SmartScreen kann deshalb beim ersten Start einen Hinweis anzeigen.

Beim Schließen des Fensters läuft das Tracking standardmäßig im Infobereich weiter. Vollständig beendet wird die App über **Beenden** im Tray-Menü.

Bei einer neuen Installation führt ein kurzer Assistent durch die wichtigsten Tracking- und Windows-Optionen. Er lässt sich später unter **Einstellungen → Windows & Tray** erneut öffnen. Bestehende Installationen werden durch ein Update nicht ungefragt in den Assistenten versetzt.

## Lokale Daten und Datenschutz

Alle dauerhaften Daten liegen unter `%LocalAppData%\YFTimeTracker`:

| Pfad | Inhalt |
| --- | --- |
| `yftimetracker.db` | Spiele, EXE-Zuordnungen, Sessions und Einstellungen |
| `Backups` | automatische und manuelle Sicherungen |
| `Exports` | vom Benutzer erstellte Exporte |
| `GameIcons` | lokal aus Spiel-EXE-Dateien extrahierte Icon-Kopien |
| `Logs` | lokale Diagnoseprotokolle |

Das Diagnose-ZIP enthält Systeminformationen und höchstens drei aktuelle Logdateien, aber keine Datenbank, Backups oder Spielsessions.

## Automatisches Tracking

Manuell registrierte EXE-Pfade werden direkt erkannt. Zusätzlich aktualisiert YFTimeTracker den lokalen Launcher-Katalog beim Start und anschließend alle fünf Minuten. Ist bei einer Launcher-Installation keine eindeutige Startdatei bekannt, muss ein passender Prozess innerhalb des Installationsordners in zwei aufeinanderfolgenden Scans laufen. Hilfsprogramme wie Launcher, Uninstaller, Crash Reporter und Anti-Cheat-Installer werden ausgeschlossen.

Mehrere Prozesse desselben Spiels werden zu einer Session zusammengefasst. Prozessneustarts erzeugen getrennte Sessions. Nach einem App-Absturz wird eine offene Session nur dann fortgesetzt, wenn das Spiel im selben Windows-Start weiterhin läuft; andernfalls endet sie am letzten gespeicherten Lebenszeichen.

Wechselt Windows in den Energiesparmodus, endet eine laufende Session zum gemeldeten Zeitpunkt des Wechsels, und nach dem Aufwachen beginnt für ein weiterhin laufendes Spiel eine neue Session. Meldet Windows den Wechsel nicht – das kommt bei modernem Standby (S0) vor –, greift weiterhin die Erkennung über die ausgefallenen Scans: Die Session endet dann beim letzten beobachteten Scan. Kurze Schlafphasen unterhalb dieser Schwelle können in diesem Fall noch als Spielzeit gezählt werden.

Die manuellen Prüfschritte stehen im [Tracking-Smoke-Test](docs/TRACKING_SMOKE_TEST.md).

## Entwicklung

Voraussetzungen:

- Windows 11 x64 ab Build 22621
- .NET 10 SDK
- PowerShell

```powershell
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
dotnet run --project YFTimeTracker.App\YFTimeTracker.App.csproj
```

Die Befehle müssen einzeln ausgeführt werden. Insbesondere dürfen `dotnet test` und `dotnet run` nicht ohne Zeilenumbruch hintereinander stehen.

### Projektstruktur

- `YFTimeTracker.Core`: Domain-Modelle, Schnittstellen, Tracking-Regeln, Statistik und Validierung
- `YFTimeTracker.Data`: Entity Framework Core, SQLite, Migrationen, Repositories, Backups, Import und Export
- `YFTimeTracker.Windows`: Windows-Prozesse, Pfade, Boot-Sitzung und lokale Launcher-Erkennung
- `YFTimeTracker.App`: WinUI-3-Oberfläche, Tray, Autostart, Updates und Diagnose
- `YFTimeTracker.Core.Tests`: Tests für Tracking, Statistik und Domain-Logik
- `YFTimeTracker.Data.Tests`: Tests für Persistenz, Migration, Backup und Import/Export
- `YFTimeTracker.Windows.Tests`: Tests für Launcher-Metadaten und Windows-nahe Logik

## Lokales Release-Paket

Ein reproduzierbares, selbstenthaltendes Windows-x64-Paket lässt sich lokal erstellen:

```powershell
.\scripts\New-Release.ps1 -Version 0.5.0
```

Das Skript erzeugt die App-Assets neu, stellt NuGet-Pakete wieder her, führt den Release-Testlauf aus und schreibt das portable ZIP samt SHA-256-Prüfsumme nach `artifacts\release`. Setup, MSI und Velopack-Updatepakete werden anschließend in der GitHub-Release-Pipeline erzeugt.

## Branches und automatische Releases

Neue Änderungen werden auf `develop` entwickelt und anschließend nach `main` gemergt. Jeder Push nach `main` startet die semantische Versionierung und – sofern nicht übersprungen – ein neues GitHub Release. Conventional-Commit-Nachrichten bestimmen die Versionsstufe:

- `fix:` → Patch-Version
- `feat:` → Minor-Version
- `feat!:` oder `BREAKING CHANGE:` → Major-Version
- `[skip release]` → keine Veröffentlichung

Ein Release enthält Setup, MSI, Velopack-Pakete, portables ZIP, SHA-256-Prüfsumme und Release-Manifest. Details enthält [CONTRIBUTING.md](CONTRIBUTING.md).

## Automatische Updates

Installierte Setup- und MSI-Ausgaben prüfen beim Start den stabilen öffentlichen GitHub-Release-Kanal. Eine manuelle Prüfung ist unter **Einstellungen → App-Updates** und im Tray-Menü möglich. Verfügbare Updates werden erst nach Bestätigung heruntergeladen, zeigen ihren Fortschritt an und werden nach einem kontrollierten Neustart installiert.

Es wird kein GitHub-Token in Quellcode, Build oder Anwendung eingebettet. Vorabversionen werden nicht automatisch angeboten.

## Mitwirken und Support

Hinweise zu Branches, Commits, Tests und Pull Requests stehen in [CONTRIBUTING.md](CONTRIBUTING.md). Fehlerberichte sollten die betroffene Version, die Schritte zur Reproduktion und – sofern sinnvoll – das unter **Einstellungen → Diagnose & Support** erzeugte Diagnose-ZIP enthalten.

## Lizenz

YFTimeTracker steht unter der [MIT-Lizenz](LICENSE).
