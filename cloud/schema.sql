-- FREDI test API, schema v1. Access only through the authenticated Edge gateway.
-- This is the reproducible schema source; deploy with Supabase apply_migration.
create table public.fredi_users (
 dataset uuid not null, id integer not null, username text not null,
 payload jsonb not null, primary key(dataset,id), unique(dataset,username)
);
create table public.fredi_sessions (
 token_hash text primary key, dataset uuid not null, user_id integer not null,
 security_version bigint not null, expires_at timestamptz not null,
 touched_at timestamptz not null default now()
);
create index on public.fredi_sessions(dataset,user_id);
create table public.fredi_login_limits (
 key text primary key, starts_at timestamptz not null default now(), attempts integer not null default 0
);
create table public.fredi_scopes (
 dataset uuid not null, scope text not null, revision bigint not null default 0,
 updated_at timestamptz not null default now(), primary key(dataset,scope)
);
create table public.fredi_records (
 dataset uuid not null, scope text not null, entity text not null, local_key text not null,
 payload jsonb not null, primary key(dataset,scope,entity,local_key)
);
create table public.fredi_sync_batches (
 dataset uuid not null, request_id uuid not null, scope text not null, revision bigint not null,
 user_id integer not null, received_at timestamptz not null default now(), payload jsonb not null,
 primary key(dataset,request_id), unique(dataset,scope,revision)
);
-- Session tokens/password verifiers are never readable with publishable/anon keys.
alter table public.fredi_users enable row level security;
alter table public.fredi_sessions enable row level security;
alter table public.fredi_login_limits enable row level security;
alter table public.fredi_scopes enable row level security;
alter table public.fredi_records enable row level security;
alter table public.fredi_sync_batches enable row level security;
revoke all on public.fredi_users,public.fredi_sessions,public.fredi_login_limits,public.fredi_scopes,public.fredi_records,public.fredi_sync_batches from anon,authenticated;
grant all on public.fredi_users,public.fredi_sessions,public.fredi_login_limits,public.fredi_scopes,public.fredi_records,public.fredi_sync_batches to service_role;
create policy deny_client_access on public.fredi_users for all to anon,authenticated using(false) with check(false);
create policy deny_client_access on public.fredi_sessions for all to anon,authenticated using(false) with check(false);
create policy deny_client_access on public.fredi_login_limits for all to anon,authenticated using(false) with check(false);
create policy deny_client_access on public.fredi_scopes for all to anon,authenticated using(false) with check(false);
create policy deny_client_access on public.fredi_records for all to anon,authenticated using(false) with check(false);
create policy deny_client_access on public.fredi_sync_batches for all to anon,authenticated using(false) with check(false);

-- La función fredi_gateway está en gateway.sql; aplíquela después de este archivo.
