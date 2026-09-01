# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This repository also has an `AGENTS.md` with binding rules for coding agents (branch policy, data-safety rules, UI rules). Read it — the summary below folds in the parts most relevant to day-to-day changes, but `AGENTS.md` is the source of truth if anything conflicts.

## Project

YFTimeTracker is a German-language, fully local Windows 11 desktop app (WinUI 3) that automatically detects and tracks game playtime by watching running processes and local Steam/Epic/GOG/Xbox installations. No cloud accounts, no web APIs for game detection, no telemetry.

## Commands

All commands run from the repository root in PowerShell. Requires Windows 11 x64 (build 22621+) and the .NET 10 SDK.

```powershell
dotnet restore YFTimeTracker.slnx --configfile NuGet.config
dotnet build YFTimeTracker.slnx --no-restore
dotnet test YFTimeTracker.slnx --no-build --no-restore
dotnet run --project YFTimeTracker.App\YFTimeTracker.App.csproj
```

Run these as separate commands (not chained on one line) — `dotnet test` and `dotnet run` must not run back-to-back without a line break.

Run a single test (MSTest, filter by fully-qualified name or a substring):

```powershell
dotnet test YFTimeTracker.slnx --no-build --no-restore --filter "FullyQualifiedName~GameTrackingServiceTests"
```

Restore is only needed after changing package references; otherwise build/test without `--restore` is fine day to day.

Build a local, reproducible self-contained release package (also runs the full test suite unless `-SkipTests` is passed):

```powershell
.\scripts\New-Release.ps1 -Version 0.5.0
```

Regenerate app icon/asset variants after changing `YFTimeTrackerLogo.png`:

```powershell
.\scripts\Generate-AppAssets.ps1
```

## Architecture

Four layered class libraries plus one test project per layer, referenced top-down only (no project references upward):

- **`YFTimeTracker.Core`** — domain models, abstractions (interfaces), tracking rules, statistics, validation. No dependency on WinUI, SQLite, or concrete Windows APIs; this is what makes tracking logic unit-testable without a UI or database.
- **`YFTimeTracker.Data`** — EF Core + SQLite. Repositories, migrations, JSON+ZIP backup/import/export. Implements the `Core` repository/store abstractions.
- **`YFTimeTracker.Windows`** — Windows-specific implementations of `Core` abstractions: process snapshots, local launcher discovery (Steam/Epic/GOG/Xbox), registry, boot-session id, autostart.
- **`YFTimeTracker.App`** — WinUI 3 shell: pages/views, ViewModels (CommunityToolkit.Mvvm `ObservableObject` + `AsyncRelayCommand`), tray icon, single-instance handling, first-run setup wizard, Velopack auto-update, diagnostics export.
- **`YFTimeTracker.Core.Tests` / `YFTimeTracker.Data.Tests` / `YFTimeTracker.Windows.Tests`** — MSTest, mirror the layer they test. There is no `App.Tests` project; UI-adjacent logic that needs testing generally belongs in a lower layer.

New logic goes in the lowest layer that can host it. UI code must not duplicate persistence or process-tracking logic.

### Composition root

`YFTimeTracker.App/App.xaml.cs` (`OnLaunched`) builds a generic `Host` and wires every layer in one place: `services.AddYFTimeTrackerCore()` (Core), `AddYFTimeTrackerWindowsServices()` (Windows), `AddYFTimeTrackerData()` (Data), then registers App-layer services, pages (transient) and ViewModels (mostly singleton; `GameDetailsViewModel` is transient). Each layer exposes its own `IServiceCollection` extension method (`CoreServiceCollectionExtensions`, `DataServiceCollectionExtensions`, `WindowsServiceCollectionExtensions`) — add new services there, not ad hoc in `App.xaml.cs`.

Static `App.Services` is the service locator used by views (e.g. `MainWindow` resolves its ViewModel and services via `App.Services.GetRequiredService<T>()` in its constructor rather than through DI-injected page constructors).

### Tracking engine

`GameTrackingService` (`YFTimeTracker.Core/Services/GameTrackingService.cs`) is the core state machine and the most intricate piece of the codebase:

- Runs a `PeriodicTimer`-driven scan loop (`ScanOnceCoreAsync`) at a configurable interval, guarded by a single `SemaphoreSlim` gate so start/stop/pause/resume/scan never interleave.
- Matches running processes against known games' registered executables, and separately against a periodically refreshed local launcher catalog (`IGameInstallationProvider.DiscoverAsync`, refreshed every 5 minutes) to auto-discover launcher-installed games. Launcher matches require confirmation across two consecutive scans unless the process is an explicit launch executable — this avoids false positives from short-lived helper processes.
- Excludes known helper executables (launchers, updaters, anti-cheat, crash reporters) via name/prefix/suffix/substring lists so they never open a session on their own.
- Merges multiple processes/executables of the same game into a single session; a process restart splits into a new session instead of extending the old one.
- On startup, recovers sessions left open by a crash: continues them only if the same game is still running in the same Windows boot session (`IBootSessionProvider`); otherwise closes them at the last recorded heartbeat.
- Detects unobserved gaps (e.g. system standby) by comparing against the last successful scan timestamp and splits/closes sessions at the gap boundary rather than counting sleep time as playtime.
- Publishes `TrackingState` via a `StateChanged` event that the App layer polls/subscribes to for the dashboard and tray.

When changing this file, prefer adding to `YFTimeTracker.Core.Tests/Services/GameTrackingServiceTests.cs` over reasoning about it in isolation — the existing tests encode a lot of the intended edge-case behavior (restarts, gaps, crash recovery, duplicate open sessions).

### Data layer conventions

- `IAppPathProvider` (Windows layer) resolves `%LocalAppData%\YFTimeTracker` and its subfolders (db, `Backups`, `Exports`, `Logs`); the SQLite connection string is built from it in `DataServiceCollectionExtensions`.
- EF Core migrations live in `YFTimeTracker.Data/Migrations`; `DesignTimeDbContextFactory` supplies a design-time context pointed at the real local-appdata db path for `dotnet ef` tooling. Schema changes require a migration plus tests against an existing (pre-migration) database — persisted user data must survive upgrades.
- Backup/export format is versioned JSON inside a ZIP (`YFTimeTracker.Data/Backup/JsonZipBackupService.cs` + `BackupDocument`). Changes to the export format must keep reading version-1 exports unless a breaking change is explicitly agreed.
- The automatic backup flow must not be bypassed ahead of a database migration.

## Project-specific rules worth knowing before editing

- All user-visible UI text is German, with correct umlauts. Not-yet-implemented areas must be clearly labeled **Vorschau** or **Demnächst**, never faked with placeholder data.
- Reuse the existing dark neon design and shared resources in `YFTimeTracker.App/App.xaml` rather than introducing new styling; don't add new UI/diagramming dependencies without an explicit decision.
- New views must work at both wide and narrow window widths.
- Background refreshes (dashboard timer, tracking scans) must not visibly flicker lists or reset scroll position/selection.
- Manually registered games (`GameSource.Manual`) must keep working even when launcher data is missing or corrupt.
- Multiple processes or executables belonging to one game must never produce more than one concurrent open session.
- Tracking pause must not import games or open new sessions.
- No secrets, access tokens, or personal file paths in source or commits. No GitHub token is ever embedded in the app; auto-update only checks the public stable release channel and never offers prereleases.
- Release artifacts are intentionally unsigned; code signing is out of scope for this project.
- When a change adds, removes, or materially changes a user-facing feature (new launcher support, export formats, theme options, etc.), update the `README.md` feature list (`## Funktionen`) in the same change so it doesn't drift from what the app actually does.

## Branching and releases

- `develop` is the working branch; `main` is the published state. Default to working on `develop` — do not merge to `main` without being asked.
- A push/merge to `main` triggers `.github/workflows/auto-tag.yml`, which computes the next semantic version from Conventional Commit messages since the last tag and calls `.github/workflows/release.yml` to build, test, and publish a GitHub release (Setup, MSI, Velopack update packages, portable ZIP, SHA-256 checksum, release manifest).
- Conventional Commits drive the version bump: `fix:` → patch, `feat:` → minor, `feat!:` or a `BREAKING CHANGE:` footer → major, anything else → patch by default. `[skip release]` in the head commit message skips the release entirely.
- Package versions are centrally managed in `Directory.Packages.props` — don't pin versions in individual `.csproj` files.

## Manual verification

Automated tests don't cover process/launcher detection, tray behavior, or the update flow. After changes in those areas, additionally check the relevant scenarios in [docs/TRACKING_SMOKE_TEST.md](docs/TRACKING_SMOKE_TEST.md) and, per `CONTRIBUTING.md`, manually verify whichever of these apply: wide/narrow windows, empty vs. populated database, tracking start/end/pause + tray, launcher detection (Steam/Epic/GOG/Xbox), backup/import/export, installed-build update flow, single-instance/autostart/minimized start, first-run setup on empty vs. upgraded database.
