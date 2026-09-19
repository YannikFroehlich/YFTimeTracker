-- =============================================================================
-- YFTimeTracker - Supabase-Schema (Schemaversion 2)
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
    device_id           uuid references public.devices (id) on delete set null,
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
    device_id        uuid references public.devices (id) on delete set null,
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
    device_id      uuid references public.devices (id) on delete set null,
    started_at     timestamptz not null default now(),
    finished_at    timestamptz,
    app_version    text,
    schema_version integer     not null default 2,
    uploaded       integer     not null default 0,
    downloaded     integer     not null default 0,
    conflicts      integer     not null default 0,
    status         text        not null default 'running',
    error_message  text
);

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
