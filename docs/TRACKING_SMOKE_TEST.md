# Tracking-Smoke-Test

Diese kurze Prüfung deckt Windows-Abläufe ab, die sich nicht vollständig mit Unit-Tests simulieren lassen. Vor dem Test sollte unter **Einstellungen → Sicherung** ein Backup erstellt werden.

## Pro Launcher

Die Schritte einmal mit einem installierten Steam-, Epic- beziehungsweise GOG-Spiel durchführen:

1. YFTimeTracker starten und unter **Einstellungen → Launcher-Erkennung** prüfen, ob der Launcher als verfügbar erscheint.
2. Ein noch nicht importiertes Spiel starten. Es muss nach spätestens zwei Tracking-Scans auf dem Dashboard erscheinen und erst dann in die Bibliothek aufgenommen werden.
3. Das Hauptfenster schließen. Das Tray-Symbol muss bestehen bleiben und das aktive Spiel im Tooltip anzeigen.
4. Das Fenster nach mindestens einer Minute über das Tray-Symbol wieder öffnen. Die Laufzeit muss weitergelaufen sein.
5. Das Spiel vollständig beenden. Sobald alle zugehörigen Prozesse beendet sind, muss die Session geschlossen werden.
6. Das Spiel erneut starten. Es muss eine neue Session entstehen; die vorherige darf nicht verlängert werden.

## Unterbrechungen

1. **Tracking pausieren:** Während der Pause ein noch unbekanntes Launcher-Spiel starten. Es darf weder importiert noch als Session erfasst werden. Nach dem Fortsetzen beginnt die Erfassung ab diesem Zeitpunkt.
2. **Standby:** Mit laufendem Spiel Windows für mehr als zwei Minuten in den Standby versetzen. Nach dem Aufwachen entsteht eine neue Session; die unbeobachtete Standby-Zeit wird nicht gezählt.
3. **App-Absturz simulieren:** Die App bei laufendem Spiel über den Task-Manager beenden und direkt neu starten. Im selben Windows-Start wird die offene Session fortgesetzt. Wurde das Spiel zwischenzeitlich beendet, endet sie am letzten gespeicherten Lebenszeichen.
4. **Tray-Beenden:** Über **Beenden** im Tray schließen. Die aktuelle Session muss sauber enden, auch wenn das Spiel weiterläuft.

Bei Abweichungen unter **Einstellungen → Diagnose & Support** ein Diagnose-ZIP erstellen. Die relevanten Protokolleinträge heißen unter anderem `Started session`, `Closed session`, `Continued open session`, `Split session` und `Tracking scan failed`.
