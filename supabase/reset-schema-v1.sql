-- =============================================================================
-- Einmaliger Wechsel von Schemaversion 1 auf 2
--
-- ACHTUNG: Dieses Skript loescht Tabellen samt Inhalt.
--
-- Version 1 war geraetezentriert: jede Zeile gehoerte einem (Benutzer, Geraet)
-- und die lokale SQLite-Id war Teil des Schluessels. Version 2 bindet die Daten
-- an das Konto, damit sich auf einem zweiten PC dieselbe Bibliothek zeigt. Beide
-- Formen lassen sich nicht ineinander ueberfuehren, ohne die Identitaeten neu zu
-- berechnen - und das macht die App beim ersten Abgleich ohnehin selbst.
--
-- Nur ausfuehren, wenn in Version 1 noch keine Daten stehen, die nicht auch
-- lokal auf einem PC liegen. Im Zweifel vorher pruefen:
--
--   select
--       (select count(*) from public.games)         as spiele,
--       (select count(*) from public.game_sessions) as sessions;
--
-- Danach supabase/schema.sql einspielen.
-- =============================================================================

drop table if exists public.game_artwork      cascade;
drop table if exists public.game_sessions     cascade;
drop table if exists public.game_tags         cascade;
drop table if exists public.game_executables  cascade;
drop table if exists public.tracking_exclusions cascade;
drop table if exists public.app_settings      cascade;
drop table if exists public.backup_runs       cascade;
drop table if exists public.games             cascade;
drop table if exists public.devices           cascade;

-- Der Storage-Bucket und seine Policy bleiben bestehen; Version 2 nutzt beide
-- unveraendert weiter. Vorhandene Cover-Dateien werden beim naechsten Abgleich
-- ueberschrieben oder verwaisen und koennen dann von Hand entfernt werden.
