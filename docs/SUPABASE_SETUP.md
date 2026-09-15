# YFDatenbank: Kontoabgleich über Supabase

YFTimeTracker kann Spiele, Spielzeiten und Einstellungen in einem Konto
speichern. Meldet man sich auf einem zweiten PC mit demselben Konto an, stehen
dort dieselbe Bibliothek und alle Spielzeiten zur Verfügung.

Die Funktion ist **optional und standardmäßig aus**. Ohne Anmeldung verhält sich
die App unverändert: alles bleibt lokal, nichts wird übertragen. Die
Spielerkennung selbst arbeitet in jedem Fall rein lokal und fragt keine Web-API.

## Für Benutzer

Das Konto sitzt im **Profil oben rechts**, nicht in den Einstellungen. Das
Sicherungsziel unter *Einstellungen → Daten & Sicherung* steuert etwas anderes:
wohin die tägliche lokale Sicherungs**datei** zusätzlich kopiert wird.

1. Profil-Avatar oben rechts anklicken
2. E-Mail und Passwort eingeben, **Konto anlegen**
3. Bestätigungslink aus der Supabase-Mail öffnen
4. Zurück in der App **Anmelden**

Direkt nach der Anmeldung läuft der erste Abgleich. Danach gleicht die App bei
jedem Start automatisch ab; **Jetzt abgleichen** im Profil stößt es von Hand an.

> Der Bestätigungslink leitet nach erfolgreicher Bestätigung auf
> `localhost:3000` weiter. Weil dort nichts läuft, zeigt der Browser
> `ERR_CONNECTION_REFUSED`. Die Bestätigung ist zu diesem Zeitpunkt bereits
> erfolgt — die Fehlerseite betrifft nur die Weiterleitung.

### Was im Konto landet

| Wandert mit | Bleibt auf diesem PC |
|---|---|
| Spiele, EXE-Zuordnungen, Tags | Autostart, Sicherungsziel und -ordner |
| Sessions (Spielzeiten) | Zuletzt gesehener Changelog, Update-Erinnerungen |
| Erkennungsausschlüsse | Suchverlauf, Ersteinrichtungs-Status |
| Cover-Bilder | Angemeldete E-Mail, Zeitpunkt des letzten Abgleichs |
| Anzeigename und Akzentfarbe | |
| Übrige Einstellungen (Theme, Intervalle …) | |

Abmelden lässt die lokalen Daten unberührt.

## Für eigene Builds

Der ausgelieferte Build bringt die Verbindung zu einem Supabase-Projekt mit, damit
Benutzer sich schlicht anmelden können. Wer die App selbst baut, braucht ein
eigenes Projekt.

### 1. Schema einspielen

**SQL Editor → New query**, Inhalt von [`supabase/schema.sql`](../supabase/schema.sql)
einfügen, **Run**. Das Skript ist idempotent und legt Tabellen, Indizes,
Trigger, RLS-Policies und den Storage-Bucket an.

Danach sollten unter **Table Editor** zehn Tabellen stehen, jede mit aktivem RLS.

> **Wichtig:** Ohne die Row-Level-Security-Policies aus dem Skript könnte jeder
> angemeldete Benutzer des Projekts alle Daten lesen. Niemals ohne diesen Block
> einspielen.

Wer von Schemaversion 1 kommt (geräteorientiert, vor dem Kontoabgleich), spielt
vorher [`supabase/reset-schema-v1.sql`](../supabase/reset-schema-v1.sql) ein. Das
Skript löscht die alten Tabellen samt Inhalt — die beiden Formen lassen sich
nicht ineinander überführen.

### 2. Verbindung hinterlegen

**Project Settings → API Keys** liefert:

- **Project URL** — `https://<projektkennung>.supabase.co`
- **Publishable key** — `sb_publishable_…`; in älteren Projekten heißt derselbe
  Schlüssel **anon key** und ist ein JWT. Beide Formen funktionieren.

Den **Secret Key** (früher `service_role`) braucht die App nicht. Er umgeht RLS
und gehört nie in eine Desktop-Anwendung.

Beides kommt in `YFTimeTracker.App/cloud.config.json`, Vorlage liegt als
`cloud.config.example.json` daneben:

```json
{
  "projectUrl": "https://beispiel.supabase.co",
  "publishableKey": "sb_publishable_..."
}
```

Die Datei ist gitignoriert (siehe `AGENTS.md`: keine Schlüssel im Repository) und
wird beim Bauen ins Ausgabeverzeichnis kopiert. Fehlt sie, bleibt der
Kontoabgleich einfach aus — die App läuft unverändert lokal.

### 3. Bestätigungsmail abschalten (optional)

Für ein Einzelnutzer-Projekt: **Authentication → Sign In / Providers → Email** →
*Confirm email* aus. Dann meldet „Konto anlegen" direkt an.

## Wie der Abgleich arbeitet

### Identität statt Zeilennummer

Die lokale SQLite-Id taugt nicht als Schlüssel: Spiel 5 auf dem einen PC ist
nicht Spiel 5 auf dem anderen. Stattdessen bekommt jeder Datensatz eine
Identität, die beide Geräte unabhängig voneinander auf denselben Wert berechnen:

| Datensatz | Identität | Beispiel |
|---|---|---|
| Launcher-Spiel | Quelle und Launcher-Id | `game:steam:346110` |
| Manuelles Spiel | normalisierter Name | `game:manual:mein spiel` |
| EXE-Datei | Spiel und normalisierter Pfad | `exe:game:steam:346110:c:\…\game.exe` |
| Session | Gerät, Spiel und Startzeit | `ses:<machinekey>:game:steam:346110:…` |

Nach dem ersten Abgleich wird die Identität **lokal festgeschrieben** und nicht
mehr neu berechnet. Sonst änderte sich die einer Session, sobald sie auf einem
zweiten PC liegt — ihre Identität enthält den MachineKey des Ursprungsgeräts —
und dieselbe Session würde ein zweites Mal hochgeladen.

Der MachineKey ist ein Hash aus Windows-MachineGuid und Benutzernamen. Zwei
Windows-Konten auf demselben PC bleiben damit getrennte Geräte, und die rohe
MachineGuid verlässt den Rechner nicht.

### Änderungserkennung über Inhalts-Hashes

Jeder Datensatz speichert lokal den Hash, mit dem er zuletzt abgeglichen war.
Weicht der aktuelle Hash ab, wurde hier geändert; weicht der Hash im Konto ab,
dort. Das kommt ohne zusätzliche Zeitstempel in jedem Schreibpfad aus.

Der Hash deckt nur die abgeglichenen Felder ab. Lokale Id und
Installationsverzeichnis gehen nicht ein — sie sind gerätespezifisch und sollen
keinen Abgleich auslösen.

### Löschen braucht Grabsteine

Wird etwas gelöscht, bleibt ein Grabstein zurück, bis die Löschung im Konto
angekommen ist. Ohne ihn wäre „fehlt lokal" nicht von „auf dem anderen PC neu
angelegt" zu unterscheiden — und jedes gelöschte Spiel käme beim nächsten
Abgleich zurück.

Im Konto wird nie wirklich gelöscht, sondern nur `deleted_at` gesetzt. Verschwände
die Zeile, könnte der zweite PC sie nicht von „hier noch nie angekommen"
unterscheiden. Beim erneuten Hochladen desselben Datensatzes wird `deleted_at`
wieder aufgehoben.

Datensätze, die nie abgeglichen wurden, erzeugen bewusst keinen Grabstein — im
Konto haben sie nie existiert.

### Konflikte

Wird derselbe Datensatz auf beiden Seiten zwischen zwei Abgleichen geändert,
**gewinnt der Stand aus dem Konto**. Die lokale Änderung wird verworfen, der Fall
aber gezählt, im Profil angezeigt und ins Log geschrieben — nicht still
überschrieben.

Bei Spielzeiten ist das praktisch ohne Bedeutung: Sessions entstehen je Gerät und
kollidieren nie. Betreffen kann es Dinge wie Name, Tags oder Limits eines Spiels.

### Reihenfolge und Ausfallsicherheit

Erst hochladen, dann übernehmen. Bricht ein Lauf dazwischen ab, sind die eigenen
Daten bereits im Konto; der lokale Bestand bleibt unverändert und der nächste Lauf
holt die Gegenrichtung nach. Der Abgleich ist nie Voraussetzung für das Tracking —
ist Supabase nicht erreichbar, läuft die App unverändert weiter.

## Was wo gespeichert wird

| Wert | Ort | Begründung |
|---|---|---|
| Passwort | nirgends | Geht nur an den einen Anmeldeaufruf |
| Refresh-Token | Windows-Anmeldeinformationsspeicher | Nicht in Exporten oder Diagnosepaketen |
| Access-Token | nur im Arbeitsspeicher | Lebt eine Stunde |
| Publishable Key | `cloud.config.json` neben der App | Nicht im Repository |
| E-Mail, Zeitpunkt des letzten Abgleichs | lokale Einstellungstabelle | Unkritisch, in der Diagnose hilfreich |

## Kosten

Auf dem kostenlosen Plan (500 MB Datenbank, 1 GB Storage) reicht der Platz für
viele Jahre Spielzeitdaten: Spiele und Sessions sind Textzeilen von wenigen
hundert Byte. Der Platzbedarf wird praktisch nur von den Cover-Bildern bestimmt.

Supabase pausiert Projekte auf dem kostenlosen Plan nach einer Woche ohne
Zugriff. Der Abgleich beim App-Start verhindert das im Normalbetrieb.

## Fehlersuche

| Meldung | Ursache |
|---|---|
| „Für dieses Build ist kein Supabase-Projekt hinterlegt." | `cloud.config.json` fehlt oder ist unvollständig |
| „Die E-Mail-Adresse ist noch nicht bestätigt." | Bestätigungslink noch nicht geöffnet |
| „Anmeldung fehlgeschlagen" | Falsches Passwort, oder das Konto existiert nicht |
| `row-level security policy` im Fehlertext | Schema-Skript nicht (vollständig) eingespielt |
| „Supabase ist nicht erreichbar." | Keine Verbindung, oder Projekt-URL falsch |
| Abgleich meldet dauerhaft Zeilen, die nie ankommen | Untergeordnete Zeilen ohne ihr Spiel im Konto |

Details stehen im Log unter `%LocalAppData%\YFTimeTracker\Logs`. Der Abgleich
protokolliert jeden Lauf mit Richtung, Anzahl und Konflikten.
