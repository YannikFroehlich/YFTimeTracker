# Entwicklung und Releases

## Branch-Modell

- `develop` ist der Arbeitsbranch fuer neue Funktionen und Fehlerbehebungen.
- Fertige Aenderungen werden per Pull Request von `develop` nach `main` gemergt.
- `main` repraesentiert den jeweils veroeffentlichten Stand.

Direkte Pushes nach `main` sollten vermieden werden. Die GitHub-Pipeline startet
bei jedem Push nach `main`, also auch nach einem Merge.

## Commit- und PR-Titel

YFTimeTracker verwendet Conventional Commits zur automatischen semantischen
Versionierung. Bei einem Squash-Merge ist der PR-Titel die relevante
Commit-Nachricht.

| Nachricht | Versionsaenderung | Beispiel |
| --- | --- | --- |
| `fix:` | Patch | `fix: doppelte Sessions verhindern` |
| `feat:` | Minor | `feat: Session-Editor hinzufuegen` |
| `feat!:` | Major | `feat!: Exportformat neu strukturieren` |
| `BREAKING CHANGE:` im Commit-Text | Major | Inkompatible Schnittstellenaenderung |

Andere Commit-Typen erhalten standardmaessig einen Patch-Bump. Mit
`[skip release]` in der Merge-Commit-Nachricht kann die automatische
Veroeffentlichung uebersprungen werden.

## Automatische Veroeffentlichung

Nach einem Merge nach `main`:

1. `.github/workflows/auto-tag.yml` berechnet die naechste Version.
2. `.github/workflows/release.yml` baut und testet die App.
3. Velopack erzeugt Setup, MSI sowie Updatepakete.
4. GitHub erhaelt den Tag `vX.Y.Z` und ein Release mit Installer, portablem ZIP,
   SHA-256-Pruefsumme und Release-Manifest.

Die Versionsstufe kann im Actions-Bereich auch manuell ueber den Workflow
`Auto Tag YFTimeTracker` gestartet werden.
