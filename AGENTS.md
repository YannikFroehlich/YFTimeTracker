# AGENTS.md

Diese Anweisungen gelten für das gesamte YFTimeTracker-Repository und richten sich an Coding-Agenten sowie automatisierte Entwicklungswerkzeuge.

## Projektziel

YFTimeTracker ist eine deutschsprachige, lokale Windows-11-App zum automatischen Erfassen und Auswerten von Spielzeit. Priorität haben korrekte Sessions, der Schutz vorhandener Nutzerdaten, verständliche Bedienung und ein stabiler Tray-Betrieb.

## Technischer Rahmen

- .NET 10 und C# mit aktivierten Nullable Reference Types
- WinUI 3 für die x64-Desktop-App
- Entity Framework Core mit SQLite für lokale Daten
- Velopack für Setup, MSI und automatische Updates
- MSTest für automatisierte Tests
- Keine Cloud-Konten oder Web-APIs für die Spielerkennung
- Keine Code-Signierung; Release-Artefakte bleiben unsigniert

## Schichten

- `YFTimeTracker.Core`: Domain, Schnittstellen, Validierung, Tracking und Statistik; keine Abhängigkeit von UI, SQLite oder konkreten Windows-APIs
- `YFTimeTracker.Data`: SQLite, EF Core, Repositories, Migrationen, Backup, Import und Export
- `YFTimeTracker.Windows`: Prozesse, lokale Launcher-Daten, Registry, Boot-Sitzung und weitere Windows-Integrationen
- `YFTimeTracker.App`: WinUI, ViewModels, Navigation, Tray, Updates und Diagnose
- `*.Tests`: Tests passend zur jeweils verantwortlichen Schicht

Neue Logik gehört in die niedrigste sinnvolle Schicht. UI-Code darf keine Persistenz- oder Prozesslogik duplizieren.

## Arbeitsweise

1. Vor Änderungen `git status --short --branch` prüfen und nicht zugehörige Nutzeränderungen erhalten.
2. Standardmäßig auf `develop` arbeiten. Nicht selbstständig nach `main` mergen.
3. Dateien gezielt ändern; generierte Ordner wie `bin`, `obj` und `artifacts` nicht committen.
4. Keine Commits, Pushes, Merges, Tags oder Releases ohne ausdrücklichen Auftrag des Benutzers ausführen.
5. Bei ausdrücklichem Commit-Auftrag Conventional Commits verwenden, zum Beispiel `fix: ...` oder `feat: ...`.
6. Dokumentation aktualisieren, wenn Bedienung, Datenformat, Installation oder Release-Ablauf geändert wird.

## Implementierungsregeln

- Bestehende Funktionen und persistierte Nutzerdaten müssen erhalten bleiben.
- Schemaänderungen benötigen eine EF-Core-Migration und Tests mit einer bestehenden Datenbank.
- Änderungen an Exporten müssen Version-1-Importe weiterhin unterstützen, sofern kein ausdrücklich dokumentierter Breaking Change beschlossen wurde.
- Vor Datenbankmigrationen darf der vorhandene automatische Backup-Ablauf nicht umgangen werden.
- Manuell registrierte Spiele müssen auch dann funktionieren, wenn Launcher-Daten fehlen oder beschädigt sind.
- Mehrere Prozesse oder EXE-Dateien desselben Spiels dürfen nur eine laufende Session erzeugen.
- Tracking-Pause darf weder Spiele importieren noch Sessions öffnen.
- Geheimnisse, Zugriffstokens und persönliche Pfade dürfen nicht in Quellcode oder Repository gelangen.
- Neue externe UI- oder Diagrammabhängigkeiten nur nach ausdrücklicher Entscheidung einführen.

## UI-Regeln

- Sichtbare Texte sind deutsch und verwenden korrekte Umlaute.
- Das bestehende dunkle Neon-Design und die zentralen Ressourcen in `YFTimeTracker.App/App.xaml` wiederverwenden.
- Neue Ansichten müssen bei breiten und schmalen Fenstern funktionieren.
- Echte Daten verwenden; noch nicht umgesetzte Bereiche klar als **Vorschau** oder **Demnächst** kennzeichnen.
- Regelmäßige Hintergrundaktualisierungen dürfen Listen nicht sichtbar flackern lassen und Auswahl oder Scrollposition nicht unnötig zurücksetzen.
- Änderungen an `YFTimeTrackerLogo.png` anschließend mit `scripts/Generate-AppAssets.ps1` in alle benötigten App-Assets übertragen.

## Prüfung

Nach Codeänderungen mindestens:

```powershell
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
```

Falls Pakete fehlen oder Abhängigkeiten geändert wurden, vorher ausführen:

```powershell
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
```

Bei UI-, Tracking-, Tray-, Autostart- oder Updateänderungen zusätzlich die passenden manuellen Szenarien aus `docs/TRACKING_SMOKE_TEST.md` prüfen. Fehlgeschlagene Tests nicht durch Abschwächen oder Entfernen bestehender Assertions umgehen.

## Release-Regeln

- `develop` ist der Entwicklungsbranch, `main` der veröffentlichte Stand.
- Ein Push beziehungsweise Merge nach `main` kann automatisch ein öffentliches Release auslösen.
- `fix:` erhöht Patch, `feat:` Minor und `feat!:` oder `BREAKING CHANGE:` Major.
- `[skip release]` überspringt die Veröffentlichung.
- Die Pipeline muss Setup, MSI, Updatepakete, portables ZIP, SHA-256-Prüfsumme und Release-Manifest erzeugen.
- Keine Versions-Tags oder veröffentlichten Artefakte nachträglich umschreiben.

Weitere Hinweise stehen in `README.md`, `CONTRIBUTING.md` und `docs/TRACKING_SMOKE_TEST.md`.
