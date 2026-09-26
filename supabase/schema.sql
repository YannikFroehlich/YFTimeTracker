-- =============================================================================
-- YFTimeTracker - Supabase-Schema (Schemaversion 4)
--
-- Einspielen: Supabase-Dashboard -> SQL Editor -> Inhalt einfuegen -> "Run".
-- Das Skript ist idempotent und kann gefahrlos erneut ausgefuehrt werden.
--
-- Grundgedanke: die Daten gehoeren dem Konto, nicht dem Geraet. Meldet man sich
-- auf einem zweiten PC an, sieht man dieselbe Bibliothek und alle Spielzeiten.
--
-- Identitaet statt Zeilennummer
--   Die lokale SQLite-Id taugt nicht als Schluessel: Spiel 5 auf dem einen PC
--   ist nicht Spiel 5 auf dem anderen. Stattdessen traegt jede Zeile eine
--   geraeteunabhaengige "identity", die beide Geraete unabhaengig voneinander
--   auf denselben Wert berechnen (etwa "game:steam:440").
--
-- content_hash
--   Hash genau der Felder, die abgeglichen werden. Die App vergleicht ihn mit
--   dem Hash des letzten eigenen Abgleichs und erkennt daran ohne weitere
--   Zeitstempel, welche Seite sich geaendert hat.
--
-- deleted_at statt DELETE
--   Geloescht wird nur markiert. Wuerde die Zeile wirklich verschwinden, koennte
--   der zweite PC nicht unterscheiden, ob sie geloescht oder dort noch nie
--   angekommen ist - und laedt sie beim naechsten Abgleich wieder hoch.
-- =============================================================================

create extension if not exists pgcrypto;

-- -----------------------------------------------------------------------------
-- Profil: Anzeigename und Akzentfarbe, genau eine Zeile je Konto.
-- Frueher nur lokal gespeichert, jetzt Teil des Kontos.
-- -----------------------------------------------------------------------------
create table if not exists public.profiles (
    user_id      uuid primary key references auth.users (id) on delete cascade,
    display_name text,
    accent_color text,
    updated_at   timestamptz not null default now()
);

-- -----------------------------------------------------------------------------
-- Geraete: rein informativ, damit erkennbar bleibt, wo eine Session entstand.
-- -----------------------------------------------------------------------------
create table if not exists public.devices (
    id           uuid primary key default gen_random_uuid(),
    user_id      uuid        not null references auth.users (id) on delete cascade,
    machine_key  text        not null,
    device_name  text        not null,
    app_version  text,
    created_at   timestamptz not null default now(),
    last_seen_at timestamptz not null default now(),
    constraint devices_user_machine_unique unique (user_id, machine_key)
);

-- -----------------------------------------------------------------------------
-- Spiele
-- -----------------------------------------------------------------------------
create table if not exists public.games (
    id                   uuid primary key default gen_random_uuid(),
    user_id              uuid        not null references auth.users (id) on delete cascade,
    identity             text        not null,
    content_hash         text        not null,
    name                 text        not null,
    source               smallint    not null default 0,
    external_game_id     text,
    added_at_utc         timestamptz not null,
    daily_limit_minutes  integer,
    weekly_limit_minutes integer,
    is_pinned            boolean     not null default false,
    baseline_minutes     integer,
    deleted_at           timestamptz,
    updated_at           timestamptz not null default now(),
    constraint games_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Ausfuehrbare Dateien
--
-- Pfade unterscheiden sich zwischen zwei PCs. Beide bleiben erhalten; auf jedem
-- Geraet greift ohnehin nur der dort vorhandene Pfad. device_id sagt, wo der
-- Pfad zuletzt gesehen wurde.
-- -----------------------------------------------------------------------------
create table if not exists public.game_executables (
    id                  uuid primary key default gen_random_uuid(),
    user_id             uuid        not null references auth.users (id) on delete cascade,
    identity            text        not null,
    content_hash        text        not null,
    game_identity       text        not null,
    device_id           uuid,
    executable_path     text        not null,
    executable_path_key text        not null,
    executable_name     text        not null,
    is_primary          boolean     not null default false,
    added_at_utc        timestamptz not null,
    deleted_at          timestamptz,
    updated_at          timestamptz not null default now(),
    constraint game_executables_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Tags
-- -----------------------------------------------------------------------------
create table if not exists public.game_tags (
    id            uuid primary key default gen_random_uuid(),
    user_id       uuid        not null references auth.users (id) on delete cascade,
    identity      text        not null,
    content_hash  text        not null,
    game_identity text        not null,
    tag           text        not null,
    deleted_at    timestamptz,
    updated_at    timestamptz not null default now(),
    constraint game_tags_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Sessions
--
-- Eine Session entsteht auf genau einem Geraet und ist nie "dieselbe" wie eine
-- auf einem anderen PC. In der Auswertung zaehlen alle zusammen.
-- -----------------------------------------------------------------------------
create table if not exists public.game_sessions (
    id               uuid primary key default gen_random_uuid(),
    user_id          uuid        not null references auth.users (id) on delete cascade,
    identity         text        not null,
    content_hash     text        not null,
    game_identity    text        not null,
    device_id        uuid,
    started_at_utc   timestamptz not null,
    last_seen_at_utc timestamptz not null,
    ended_at_utc     timestamptz,
    duration_seconds bigint,
    boot_session_id  text        not null default '',
    deleted_at       timestamptz,
    updated_at       timestamptz not null default now(),
    constraint game_sessions_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Tracking-Ausnahmen
-- -----------------------------------------------------------------------------
create table if not exists public.tracking_exclusions (
    id           uuid primary key default gen_random_uuid(),
    user_id      uuid        not null references auth.users (id) on delete cascade,
    identity     text        not null,
    content_hash text        not null,
    kind         smallint    not null,
    value        text        not null,
    value_key    text        not null,
    added_at_utc timestamptz not null,
    deleted_at   timestamptz,
    updated_at   timestamptz not null default now(),
    constraint tracking_exclusions_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Einstellungen
--
-- Nur geraeteunabhaengige Einstellungen. Was an den PC gebunden ist (Autostart,
-- Sicherungsordner, Cloud-Zeitstempel), filtert die App vor dem Hochladen aus.
-- -----------------------------------------------------------------------------
create table if not exists public.app_settings (
    id             uuid primary key default gen_random_uuid(),
    user_id        uuid        not null references auth.users (id) on delete cascade,
    identity       text        not null,
    content_hash   text        not null,
    key            text        not null,
    value          text        not null,
    updated_at_utc timestamptz not null,
    deleted_at     timestamptz,
    updated_at     timestamptz not null default now(),
    constraint app_settings_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Cover-Bilder: Metadaten hier, Bilddaten im Storage-Bucket "game-artwork"
-- -----------------------------------------------------------------------------
create table if not exists public.game_artwork (
    id             uuid primary key default gen_random_uuid(),
    user_id        uuid        not null references auth.users (id) on delete cascade,
    identity       text        not null,
    content_hash   text        not null,
    game_identity  text        not null,
    content_type   text        not null,
    file_extension text        not null,
    sha256         text        not null,
    storage_path   text        not null,
    updated_at_utc timestamptz not null,
    deleted_at     timestamptz,
    updated_at     timestamptz not null default now(),
    constraint game_artwork_identity_unique unique (user_id, identity)
);

-- -----------------------------------------------------------------------------
-- Protokoll der Abgleiche
-- -----------------------------------------------------------------------------
create table if not exists public.sync_runs (
    id             uuid primary key default gen_random_uuid(),
    user_id        uuid        not null references auth.users (id) on delete cascade,
    device_id      uuid,
    started_at     timestamptz not null default now(),
    finished_at    timestamptz,
    app_version    text,
    schema_version integer     not null default 3,
    uploaded       integer     not null default 0,
    downloaded     integer     not null default 0,
    conflicts      integer     not null default 0,
    status         text        not null default 'running',
    error_message  text
);

-- -----------------------------------------------------------------------------
-- Nachtraeglich ergaenzte Spalten
--
-- "create table if not exists" laesst eine bereits vorhandene Tabelle
-- unveraendert. Neue Spalten muessen deshalb einzeln nachgezogen werden, damit
-- ein aelteres Projekt dasselbe Schema bekommt wie ein frisch angelegtes.
-- -----------------------------------------------------------------------------
alter table public.games add column if not exists baseline_minutes integer;

-- -----------------------------------------------------------------------------
-- Geraeteverweise nur auf eigene Geraete
--
-- Ein einfacher Fremdschluessel auf devices(id) prueft RLS nicht: ein Konto
-- koennte die Id eines fremden Geraets eintragen. Der Schluessel ueber
-- (device_id, user_id) erzwingt, dass das Geraet demselben Konto gehoert. Wird
-- ein Geraet geloescht, wird nur device_id geleert, nie user_id.
-- Aeltere Projekte tragen noch den einfachen Schluessel "<tabelle>_device_id_fkey";
-- er wird hier ersetzt.
-- -----------------------------------------------------------------------------
do $device_refs$
declare
    target_table text;
begin
    if not exists (
        select 1 from pg_constraint
        where conname = 'devices_id_user_unique' and conrelid = 'public.devices'::regclass)
    then
        alter table public.devices add constraint devices_id_user_unique unique (id, user_id);
    end if;

    foreach target_table in array array['game_executables', 'game_sessions', 'sync_runs']
    loop
        execute format('alter table public.%I drop constraint if exists %I;',
            target_table, target_table || '_device_id_fkey');

        if not exists (
            select 1 from pg_constraint
            where conname = target_table || '_device_owner_fkey'
              and conrelid = format('public.%I', target_table)::regclass)
        then
            execute format(
                'alter table public.%I add constraint %I foreign key (device_id, user_id) '
                || 'references public.devices (id, user_id) on delete set null (device_id);',
                target_table, target_table || '_device_owner_fkey');
        end if;
    end loop;
end
$device_refs$;

-- -----------------------------------------------------------------------------
-- Indizes: der Abgleich liest je Tabelle alles zum Konto und filtert auf
-- Aenderungen seit dem letzten Lauf.
-- -----------------------------------------------------------------------------
create index if not exists games_user_idx               on public.games (user_id, updated_at desc);
create index if not exists game_executables_user_idx    on public.game_executables (user_id, updated_at desc);
create index if not exists game_executables_game_idx    on public.game_executables (user_id, game_identity);
create index if not exists game_tags_user_idx           on public.game_tags (user_id, updated_at desc);
create index if not exists game_tags_game_idx           on public.game_tags (user_id, game_identity);
create index if not exists game_sessions_user_idx       on public.game_sessions (user_id, updated_at desc);
create index if not exists game_sessions_game_idx       on public.game_sessions (user_id, game_identity);
create index if not exists game_sessions_started_idx    on public.game_sessions (user_id, started_at_utc desc);
create index if not exists tracking_exclusions_user_idx on public.tracking_exclusions (user_id, updated_at desc);
create index if not exists app_settings_user_idx        on public.app_settings (user_id, updated_at desc);
create index if not exists game_artwork_user_idx        on public.game_artwork (user_id, updated_at desc);
create index if not exists sync_runs_user_idx           on public.sync_runs (user_id, started_at desc);

-- -----------------------------------------------------------------------------
-- updated_at automatisch pflegen
--
-- Der Client setzt den Wert nicht selbst: eine falsch gehende Uhr auf einem PC
-- wuerde sonst die Reihenfolge der Aenderungen verfaelschen. Die Serverzeit ist
-- fuer alle Geraete dieselbe.
-- -----------------------------------------------------------------------------
create or replace function public.touch_updated_at()
returns trigger
language plpgsql
as $touch$
begin
    new.updated_at := now();
    return new;
end
$touch$;

do $triggers$
declare
    target_table text;
begin
    foreach target_table in array array[
        'profiles', 'games', 'game_executables', 'game_tags', 'game_sessions',
        'tracking_exclusions', 'app_settings', 'game_artwork'
    ]
    loop
        execute format('drop trigger if exists %I on public.%I;', target_table || '_touch_updated_at', target_table);
        execute format(
            'create trigger %I before insert or update on public.%I '
            || 'for each row execute function public.touch_updated_at();',
            target_table || '_touch_updated_at', target_table);
    end loop;
end
$triggers$;

-- =============================================================================
-- Row Level Security
--
-- Ohne diesen Block koennte jeder angemeldete Benutzer alle Daten lesen.
-- "(select auth.uid())" statt "auth.uid()" laesst Postgres den Wert einmal pro
-- Abfrage statt einmal pro Zeile auswerten.
-- =============================================================================
do $rls$
declare
    target_table text;
    policy_name  text;
begin
    foreach target_table in array array[
        'profiles', 'devices', 'games', 'game_executables', 'game_tags',
        'game_sessions', 'tracking_exclusions', 'app_settings', 'game_artwork',
        'sync_runs'
    ]
    loop
        policy_name := target_table || '_owner_access';
        execute format('alter table public.%I enable row level security;', target_table);
        execute format('drop policy if exists %I on public.%I;', policy_name, target_table);
        execute format(
            'create policy %I on public.%I for all to authenticated '
            || 'using ((select auth.uid()) = user_id) '
            || 'with check ((select auth.uid()) = user_id);',
            policy_name, target_table);
    end loop;
end
$rls$;

-- =============================================================================
-- Storage-Bucket fuer Cover-Bilder
-- Pfadschema: {user_id}/{game_identity_hash}.{ext}
-- Der erste Ordner ist die user_id - genau darauf pruefen die Policies.
-- =============================================================================
insert into storage.buckets (id, name, public)
values ('game-artwork', 'game-artwork', false)
on conflict (id) do nothing;

drop policy if exists "game_artwork_owner_access" on storage.objects;
create policy "game_artwork_owner_access" on storage.objects for all to authenticated
    using (bucket_id = 'game-artwork' and (storage.foldername(name))[1] = (select auth.uid())::text)
    with check (bucket_id = 'game-artwork' and (storage.foldername(name))[1] = (select auth.uid())::text);

-- =============================================================================
-- Storage-Bucket fuer die taegliche Sicherungsdatei (Sicherungsziel YFDatenbank)
-- Pfadschema: {user_id}/auto-{yyyyMMdd}.db
-- =============================================================================
insert into storage.buckets (id, name, public)
values ('backups', 'backups', false)
on conflict (id) do nothing;

drop policy if exists "backups_owner_access" on storage.objects;
create policy "backups_owner_access" on storage.objects for all to authenticated
    using (bucket_id = 'backups' and (storage.foldername(name))[1] = (select auth.uid())::text)
    with check (bucket_id = 'backups' and (storage.foldername(name))[1] = (select auth.uid())::text);

-- =============================================================================
-- Oeffentliche Spielerprofile (seit Schemaversion 4, fuer die Website)
--
-- Eigene Tabelle statt neuer Spalten in "profiles": die App schreibt "profiles"
-- per Upsert und koennte die oeffentlichen Einstellungen sonst ueberschreiben.
-- Jedes Konto ist privat, bis es auf der Website einen Benutzernamen waehlt und
-- das Profil ausdruecklich oeffentlich schaltet.
-- =============================================================================
create table if not exists public.player_profiles (
    user_id    uuid primary key references auth.users (id) on delete cascade,
    username   text        not null,
    is_public  boolean     not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint player_profiles_username_format check (username ~ '^[A-Za-z0-9_]{3,20}$')
);

-- Gross-/Kleinschreibung zaehlt nicht: "Yannik" und "yannik" sind derselbe Name.
create unique index if not exists player_profiles_username_unique
    on public.player_profiles (lower(username));

drop trigger if exists player_profiles_touch_updated_at on public.player_profiles;
create trigger player_profiles_touch_updated_at before insert or update on public.player_profiles
    for each row execute function public.touch_updated_at();

alter table public.player_profiles enable row level security;
drop policy if exists player_profiles_owner_access on public.player_profiles;
create policy player_profiles_owner_access on public.player_profiles for all to authenticated
    using ((select auth.uid()) = user_id)
    with check ((select auth.uid()) = user_id);

-- -----------------------------------------------------------------------------
-- Lesen fremder Profile
--
-- Die Basistabellen bleiben per RLS gesperrt. Fremde Daten gibt es nur ueber
-- die beiden Funktionen unten, die als "security definer" laufen und genau die
-- freigegebenen Summen liefern: nie user_id, E-Mail, EXE-Pfade, Geraete,
-- Einstellungen oder einzelne Sessions.
--
-- Hilfsfunktionen liegen im Schema yf_private. Das ist nicht ueber die API
-- erreichbar; sonst liesse sich die Spielzeit jedes Kontos per user_id abfragen.
-- -----------------------------------------------------------------------------
create schema if not exists yf_private;
revoke all on schema yf_private from public, anon, authenticated;

-- Spielzeit je Spiel: beendete Sessions plus Basis-Spielzeit. Spiele ohne jede
-- Spielzeit (nur in der Bibliothek erkannt) fallen heraus.
create or replace function yf_private.game_totals(p_user_id uuid)
returns table (
    name           text,
    source         smallint,
    total_seconds  bigint,
    session_count  bigint,
    last_played_at timestamptz)
language sql
stable
set search_path = ''
as $game_totals$
    select g.name,
           g.source,
           (coalesce(sum(coalesce(s.duration_seconds,
                extract(epoch from s.ended_at_utc - s.started_at_utc)::bigint)), 0)
            + coalesce(g.baseline_minutes, 0)::bigint * 60)::bigint,
           count(s.id),
           max(s.ended_at_utc)
    from public.games g
    left join public.game_sessions s
        on s.user_id = g.user_id
       and s.game_identity = g.identity
       and s.deleted_at is null
       and s.ended_at_utc is not null
    where g.user_id = p_user_id
      and g.deleted_at is null
    group by g.id, g.name, g.source, g.baseline_minutes
    having coalesce(sum(coalesce(s.duration_seconds,
                extract(epoch from s.ended_at_utc - s.started_at_utc)::bigint)), 0)
           + coalesce(g.baseline_minutes, 0) * 60 > 0
$game_totals$;

revoke all on function yf_private.game_totals(uuid) from public, anon, authenticated;

-- Suche nach oeffentlichen Spielern ueber Benutzer- oder Anzeigename.
create or replace function public.search_players(query text)
returns table (
    username      text,
    display_name  text,
    accent_color  text,
    total_seconds bigint)
language sql
stable
security definer
set search_path = ''
as $search_players$
    with pattern as (
        -- % und _ aus der Eingabe gelten woertlich, nicht als Platzhalter.
        select '%' || replace(replace(replace(trim(query), '\', '\\'), '%', '\%'), '_', '\_') || '%' as value
    )
    select p.username,
           pr.display_name,
           pr.accent_color,
           coalesce((select sum(t.total_seconds) from yf_private.game_totals(p.user_id) t), 0)::bigint
    from public.player_profiles p
    left join public.profiles pr on pr.user_id = p.user_id
    cross join pattern
    where p.is_public
      and length(trim(query)) >= 2
      and (p.username ilike pattern.value or pr.display_name ilike pattern.value)
    order by lower(p.username) = lower(trim(query)) desc, lower(p.username)
    limit 20
$search_players$;

-- Ein Profil als JSON. Liefert null, wenn es den Namen nicht gibt oder das
-- Profil privat ist - ausser fuer den Eigentuemer selbst, der sein privates
-- Profil auf der Website als Vorschau sieht.
create or replace function public.get_player_profile(p_username text)
returns jsonb
language plpgsql
stable
security definer
set search_path = ''
as $get_player_profile$
declare
    target public.player_profiles;
    today  date := (now() at time zone 'Europe/Berlin')::date;
begin
    select * into target
    from public.player_profiles
    where lower(username) = lower(trim(p_username));

    -- "is not distinct from" statt "=": ohne Anmeldung ist auth.uid() null, und
    -- "= null" ergaebe null statt false - das private Profil waere sichtbar.
    if target.user_id is null
        or not (target.is_public or target.user_id is not distinct from (select auth.uid()))
    then
        return null;
    end if;

    return (
        with games as (
            select * from yf_private.game_totals(target.user_id)
        ),
        -- ponytail: feste Zeitzone, Session zaehlt voll zum Starttag. Parameter
        -- p_tz und Aufteilen ueber Mitternacht ergaenzen, falls das je stoert.
        days as (
            select day::date as day,
                   coalesce(sum(coalesce(s.duration_seconds,
                       extract(epoch from s.ended_at_utc - s.started_at_utc)::bigint)), 0)::bigint as seconds
            from generate_series((today - 29)::timestamp, today::timestamp, interval '1 day') as day
            left join public.game_sessions s
                on s.user_id = target.user_id
               and s.deleted_at is null
               and s.ended_at_utc is not null
               and (s.started_at_utc at time zone 'Europe/Berlin')::date = day::date
               and exists (
                   select 1 from public.games g
                   where g.user_id = s.user_id
                     and g.identity = s.game_identity
                     and g.deleted_at is null)
            group by day
        )
        select jsonb_build_object(
            'username',       target.username,
            'display_name',   pr.display_name,
            'accent_color',   pr.accent_color,
            'is_public',      target.is_public,
            'member_since',   target.created_at,
            'total_seconds',  coalesce((select sum(total_seconds) from games), 0),
            'session_count',  coalesce((select sum(session_count) from games), 0),
            'last_played_at', (select max(last_played_at) from games),
            'games', coalesce((
                select jsonb_agg(jsonb_build_object(
                    'name',           name,
                    'source',         source,
                    'total_seconds',  total_seconds,
                    'session_count',  session_count,
                    'last_played_at', last_played_at) order by total_seconds desc)
                from games), '[]'::jsonb),
            'daily', (
                select jsonb_agg(jsonb_build_object('day', day, 'seconds', seconds) order by day)
                from days))
        from (select target.user_id as user_id) owner
        left join public.profiles pr on pr.user_id = owner.user_id
    );
end
$get_player_profile$;

revoke all on function public.search_players(text) from public;
revoke all on function public.get_player_profile(text) from public;
grant execute on function public.search_players(text) to anon, authenticated;
grant execute on function public.get_player_profile(text) to anon, authenticated;
