## Änderung

<!-- Kurz beschreiben, was geändert wurde und warum. -->

## Prüfung

- [ ] `dotnet build YFTimeTracker.slnx --no-restore` erfolgreich
- [ ] `dotnet test YFTimeTracker.slnx --no-build --no-restore` erfolgreich
- [ ] Manuell getestet, sofern erforderlich
- [ ] Breites und schmales Fenster bei UI-Änderungen geprüft
- [ ] Datenmigration, Backup und Import/Export bei Datenänderungen geprüft
- [ ] Dokumentation bei geändertem Verhalten aktualisiert

## Screenshots

<!-- Bei sichtbaren UI-Änderungen Vorher-/Nachher-Bilder ergänzen. Andernfalls „Nicht zutreffend“. -->

## Release

Der PR-Titel beziehungsweise die enthaltenen Conventional Commits bestimmen die nächste Version:

- `fix: ...` für einen Patch-Release
- `feat: ...` für einen Minor-Release
- `feat!: ...` oder `BREAKING CHANGE:` für einen Major-Release
- `[skip release]` in der Head-Commit-Nachricht, wenn kein Release entstehen soll

<!-- Breaking Changes, Migrationen oder besondere Installationshinweise hier nennen. -->
