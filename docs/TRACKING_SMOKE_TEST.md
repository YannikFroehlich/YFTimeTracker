# Tracking-Smoke-Test

Diese Prüfung deckt Windows-Abläufe ab, die nicht vollständig durch Unit-Tests simuliert werden können. Sie ist vor Releases mit Änderungen an Tracking, Prozess- oder Launcher-Erkennung, Tray, Autostart, Wiederherstellung oder Persistenz auszuführen.

## Vorbereitung

1. YFTimeTracker aus einem aktuellen Debug-Build oder installierten Release starten.
2. Unter **Einstellungen → Sicherung** ein Backup erstellen.
3. Unter **Einstellungen → Diagnose & Support** den Daten- und Logordner kontrollieren.
4. Tracking und Launcher-Erkennung aktivieren.
5. Für Launcher-Tests mindestens ein installiertes Steam-, Epic-, GOG- oder Xbox-/Microsoft-Store-Spiel bereithalten.

## Ersteinrichtung

1. YFTimeTracker mit einem neuen, leeren Datenordner starten. Der Einrichtungs-Assistent muss vor dem ersten Tracking-Scan erscheinen.
2. Alle vier Schritte vorwärts und rückwärts durchlaufen. Auswahl und Zusammenfassung müssen übereinstimmen.
3. Tracking, Launcher-Erkennung und Tray-Verhalten ändern, die Einrichtung abschließen und die Werte anschließend in den Einstellungen kontrollieren.
4. Die App neu starten. Der Assistent darf nicht erneut automatisch erscheinen.
5. Unter **Einstellungen → Windows & Tray** den Assistenten erneut öffnen, eine Auswahl ändern und prüfen, ob Tracking- und Tray-Zustand sofort übernommen werden.
6. Eine vorhandene Datenbank ohne Einrichtungs-Schlüssel mit dem neuen Build starten. Sie muss als bestehende Installation erkannt werden und darf den Assistenten nicht ungefragt anzeigen.

## Manuell registriertes Spiel

1. In der Bibliothek ein Spiel mit seiner echten EXE-Datei anlegen.
2. Das Spiel starten. Es muss innerhalb des konfigurierten Scanintervalls als aktiv erscheinen.
3. Einen zweiten zugeordneten Prozess oder eine alternative EXE desselben Spiels starten. Es darf keine zweite Session entstehen.
4. Alle zugeordneten Prozesse beenden. Die Session muss geschlossen werden.
5. Das Spiel erneut starten. Es muss eine neue Session entstehen; die vorherige darf nicht verlängert werden.

## Pro Launcher

Die Schritte einmal mit einem installierten Steam-, Epic-, GOG- beziehungsweise Xbox-/Microsoft-Store-Spiel durchführen:

1. Unter **Einstellungen → Launcher-Erkennung** prüfen, ob der Launcher als erkannt erscheint.
2. Ein noch nicht importiertes Spiel starten. Falls keine eindeutige Start-EXE bekannt ist, muss es nach spätestens zwei Tracking-Scans auf dem Dashboard erscheinen.
3. Kontrollieren, dass das Spiel erst beim tatsächlichen Start in die Bibliothek übernommen wurde.
4. Prüfen, dass Launcher, Uninstaller, Crash Reporter und ähnliche Hilfsprogramme keine Session auslösen.
5. Das Hauptfenster schließen. Das Tray-Symbol muss bestehen bleiben und das aktive Spiel im Tooltip anzeigen.
6. Das Fenster nach mindestens einer Minute über **Öffnen** oder per Doppelklick auf das Tray-Symbol anzeigen. Die Laufzeit muss weitergelaufen sein.
7. Das Spiel vollständig beenden. Sobald alle zugehörigen Prozesse beendet sind, muss die Session geschlossen werden.
8. Das Spiel erneut starten. Es muss eine neue Session entstehen.

Fehlende oder beschädigte Daten eines Launchers dürfen das Tracking manuell registrierter Spiele nicht beeinträchtigen.

Für Xbox-/Microsoft-Store-Spiele zusätzlich prüfen:

1. Unter **Einstellungen → Tracking** wird „Xbox / Microsoft Store“ als erkannt angezeigt.
2. Ein installiertes, aber nie gestartetes Spiel erscheint noch nicht in der Bibliothek.
3. Das Spiel über die Xbox-App oder das Startmenü starten.
4. Prüfen, dass nicht `gamelaunchhelper.exe`, sondern der tatsächliche Spielprozess übernommen wird.
5. Das Spiel beenden und kontrollieren, dass genau eine Session mit der Quelle **XBOX** gespeichert wurde.

## Pause und Unterbrechungen

1. **Tracking pausieren:** Während der Pause ein bekanntes und ein noch unbekanntes Launcher-Spiel starten. Es darf weder eine Session geöffnet noch das unbekannte Spiel importiert werden. Nach dem Fortsetzen beginnt die Erfassung ab diesem Zeitpunkt.
2. **Standby (Best Effort):** Mit laufendem Spiel Windows für mehr als zwei Minuten in den Standby versetzen. YFTimeTracker versucht, die unbeobachtete Zeit nach dem Aufwachen abzutrennen. Abhängig vom Windows-Energiesparmodus kann die Session dennoch durchlaufen; dieses Verhalten ist derzeit kein Release-Blocker.
3. **App-Absturz simulieren:** Die App bei laufendem Spiel über den Task-Manager beenden und direkt neu starten. Im selben Windows-Start wird die offene Session fortgesetzt. Ist das Spiel nicht mehr aktiv oder wurde Windows neu gestartet, endet sie am letzten gespeicherten Lebenszeichen.
4. **Tray-Beenden:** Über **Beenden** im Tray schließen. Die aktuelle Session muss sauber enden, auch wenn das Spiel weiterläuft.

## App-Lifecycle

1. **Einzelinstanz:** Bei laufender App `YFTimeTracker.App.exe` ein zweites Mal starten. Es darf kein zweiter Tracker entstehen; das vorhandene Fenster muss aktiviert werden.
2. **Autostart:** Autostart in den Einstellungen aktivieren, ab- und wieder anmelden. Die App muss minimiert starten und im Tray weiterlaufen. Danach Autostart bei Bedarf wieder deaktivieren.
3. **Schließen im Tray:** Das Fenster schließen und über das Tray erneut öffnen. Dashboard, Trackingzustand und laufende Session müssen erhalten bleiben.
4. **Update-Menü:** In einer installierten Ausgabe **Nach Updates suchen** im Tray auswählen. Der Status muss auch unter **Einstellungen → App-Updates** korrekt erscheinen.

## Globale Suche

1. Im Suchfeld der Kopfzeile mindestens zwei Zeichen eines Spielnamens oder einer zugeordneten EXE eingeben. Das Spiel und seine letzten Sessions müssen als getrennte Treffer erscheinen.
2. Einen Spieltreffer öffnen. Die App muss direkt zu den korrekten Spieldetails navigieren.
3. Einen Session-Treffer öffnen, der älter als 30 Tage ist. Die Ansicht **Sessions** muss auf **Gesamter Zeitraum** wechseln und die Session auswählen.
4. Nach `Statistik` suchen und den Bereichstreffer öffnen. Die App muss zu **Statistiken** navigieren.
5. Schnell nacheinander unterschiedliche Suchbegriffe eingeben. Veraltete Treffer dürfen die Ergebnisse der neueren Suche nicht überschreiben.

## Lokale Spiel-Icons

1. Ein Spiel mit einer erreichbaren EXE-Datei öffnen. Das Windows-Datei-Icon muss im Dashboard, in der Bibliothek, in der globalen Suche, in Sessions und in den Spieldetails erscheinen.
2. Ein Spiel mit fehlender oder verschobener EXE öffnen. Statt eines defekten Bildes müssen weiterhin die Initialen des Spiels angezeigt werden.
3. Die App neu starten. Bereits extrahierte Icons müssen ohne sichtbare Verzögerung aus dem lokalen Cache geladen werden.
4. Die primäre EXE eines Spiels in der Bibliothek ändern. Nach dem Speichern muss das Icon der neuen Datei angezeigt werden.

## Jahresrückblick

1. **Jahresrückblick** in der Navigation öffnen. Das aktuelle Jahr muss vorausgewählt sein und Gesamtspielzeit, Spieltage, Spiele und Sessions mit den Statistiken übereinstimmen.
2. Ein Jahr mit Daten auswählen. Der Monatsverlauf muss zwölf Monate enthalten; aktivster Monat, längste Session und Top-Spiele müssen aus echten Sessions stammen.
3. Zu einem anderen verfügbaren Jahr wechseln. Werte, Vorjahresvergleich und Rangliste müssen vollständig auf das gewählte Jahr wechseln.
4. Ein Jahr ohne Sessions auswählen, sofern vorhanden. Es dürfen keine Demo-Werte erscheinen; stattdessen muss der leere Zustand sichtbar sein.
5. Ein Top-Spiel anklicken. Die App muss die richtigen Spieldetails öffnen.
6. In der globalen Suche nach `Rückblick` suchen und den Bereichstreffer öffnen. Die App muss zum **Jahresrückblick** navigieren.

## Auswertung und Fehlerbericht

Nach jedem Test die zuletzt erstellte Session in **Sessions** und die Summen in **Statistiken** kontrollieren. Bei Abweichungen unter **Einstellungen → Diagnose & Support** ein Diagnose-ZIP erstellen.

Relevante Protokolleinträge heißen unter anderem `Started session`, `Closed session`, `Continued open session`, `Split session` und `Tracking scan failed`. Ein Fehlerbericht sollte App-Version, Windows-Version, Launcher, betroffene Spiel-EXE und genaue Reproduktionsschritte enthalten.
