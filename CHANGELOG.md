# Changelog

Änderungen an YFTimeTracker aus Nutzersicht. Der jeweils oberste Abschnitt wird der App einmalig beim ersten Start nach einem Update als "Was ist neu"-Dialog angezeigt.

## 1.0.5 – 2026-09-05
- Behoben: Ein geändertes Scan-Intervall wird jetzt sofort übernommen. Bisher lief das Tracking bis zum nächsten App-Start mit dem alten Intervall weiter.
- Behoben: Ein erreichtes Tages- oder Wochenlimit wird pro Zeitraum wirklich nur einmal gemeldet. Nach einem Neustart der App erschien die Meldung bisher am selben Tag erneut.
- Verbessert: Laufende Spiele werden zuverlässiger erkannt. Die Prozesserkennung kommt jetzt auch an Programme heran, die mit erhöhten Rechten oder unter Anti-Cheat-Schutz laufen, und benötigt dabei weniger Rechenzeit.

## 1.0.4 – 2026-09-05
- Verbessert: Geringerer Arbeitsspeicherverbrauch im Hintergrundbetrieb – Spiel-Icons werden jetzt in der tatsächlich benötigten Größe statt immer in voller Auflösung geladen, und Bibliotheks-, Sessions- sowie Statistikdaten bleiben nach dem Verlassen der jeweiligen Seite nicht mehr dauerhaft im Speicher.

## 1.0.0 – 2026-09-04
- Neu: Option "Minimiert starten" in den Einstellungen – die App startet auf Wunsch direkt im Tray, sowohl beim manuellen Start als auch über den Windows-Autostart.
- Verbessert: Der Einrichtungs-Assistent merkt sich jetzt, wenn er mit "Später" übersprungen wurde, und poppt danach nicht mehr bei jedem Start erneut auf.

## 0.15.0 – 2026-09-03
- Neu: Lokales Profil mit editierbarem Anzeigename und wählbarer Akzentfarbe für den Avatar, bearbeitbar per Klick auf das Profil-Icon oben rechts. Name und Farbe verlassen das Gerät nicht.
- Neu: Benachrichtigungsverlauf über das Glocken-Icon – erreichte Zeitlimits und verfügbare Updates bleiben dort nachvollziehbar, statt nur einmalig als Tray-Hinweis zu erscheinen. Das automatische "Update verfügbar"-Popup beim App-Start entfällt dafür; Updates lassen sich weiterhin über Einstellungen oder das Tray-Menü installieren.

## 0.14.0 – 2026-09-03
- Neu: Fortschrittsanzeige für Spielzeit-Limits in Bibliothek, Spieldetails und Dashboard – zeigt den heutigen Stand relativ zum Tageslimit, bevor die Benachrichtigung ausgelöst wird.

## 0.13.0 – 2026-09-03
- Neu: Tages- und Wochenlimit pro Spiel in den Spieldetails hinterlegbar. Wird ein Limit erreicht, erscheint einmalig eine Windows-Benachrichtigung im Infobereich.

## 0.12.0 – 2026-09-01
- Neuer "Was ist neu"-Dialog: zeigt die wichtigsten Änderungen einer Version einmalig beim ersten Start nach einem Update.
