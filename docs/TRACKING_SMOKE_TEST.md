# Tracking-Smoke-Test

Diese Prüfung deckt Windows-Abläufe ab, die nicht vollständig durch Unit-Tests simuliert werden können. Sie ist vor Releases mit Änderungen an Tracking, Prozess- oder Launcher-Erkennung, Tray, Autostart, Wiederherstellung oder Persistenz auszuführen.

## Vorbereitung

1. YFTimeTracker aus einem aktuellen Debug-Build oder installierten Release starten.
2. Unter **Einstellungen → Sicherung** ein Backup erstellen.
3. Unter **Einstellungen → Diagnose & Support** den Daten- und Logordner kontrollieren.
4. Tracking und Launcher-Erkennung aktivieren.
5. Für Launcher-Tests mindestens ein installiertes Steam-, Epic- oder GOG-Spiel bereithalten.

## Manuell registriertes Spiel

1. In der Bibliothek ein Spiel mit seiner echten EXE-Datei anlegen.
2. Das Spiel starten. Es muss innerhalb des konfigurierten Scanintervalls als aktiv erscheinen.
3. Einen zweiten zugeordneten Prozess oder eine alternative EXE desselben Spiels starten. Es darf keine zweite Session entstehen.
4. Alle zugeordneten Prozesse beenden. Die Session muss geschlossen werden.
5. Das Spiel erneut starten. Es muss eine neue Session entstehen; die vorherige darf nicht verlängert werden.

## Pro Launcher

Die Schritte einmal mit einem installierten Steam-, Epic- beziehungsweise GOG-Spiel durchführen:

1. Unter **Einstellungen → Launcher-Erkennung** prüfen, ob der Launcher als erkannt erscheint.
2. Ein noch nicht importiertes Spiel starten. Falls keine eindeutige Start-EXE bekannt ist, muss es nach spätestens zwei Tracking-Scans auf dem Dashboard erscheinen.
3. Kontrollieren, dass das Spiel erst beim tatsächlichen Start in die Bibliothek übernommen wurde.
4. Prüfen, dass Launcher, Uninstaller, Crash Reporter und ähnliche Hilfsprogramme keine Session auslösen.
5. Das Hauptfenster schließen. Das Tray-Symbol muss bestehen bleiben und das aktive Spiel im Tooltip anzeigen.
6. Das Fenster nach mindestens einer Minute über **Öffnen** oder per Doppelklick auf das Tray-Symbol anzeigen. Die Laufzeit muss weitergelaufen sein.
7. Das Spiel vollständig beenden. Sobald alle zugehörigen Prozesse beendet sind, muss die Session geschlossen werden.
8. Das Spiel erneut starten. Es muss eine neue Session entstehen.

Fehlende oder beschädigte Daten eines Launchers dürfen das Tracking manuell registrierter Spiele nicht beeinträchtigen.

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

## Auswertung und Fehlerbericht

Nach jedem Test die zuletzt erstellte Session in **Sessions** und die Summen in **Statistiken** kontrollieren. Bei Abweichungen unter **Einstellungen → Diagnose & Support** ein Diagnose-ZIP erstellen.

Relevante Protokolleinträge heißen unter anderem `Started session`, `Closed session`, `Continued open session`, `Split session` und `Tracking scan failed`. Ein Fehlerbericht sollte App-Version, Windows-Version, Launcher, betroffene Spiel-EXE und genaue Reproduktionsschritte enthalten.
