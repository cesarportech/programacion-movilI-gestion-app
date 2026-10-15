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

create or replace function public.fredi_gateway(op text, args jsonb) returns jsonb
language plpgsql security invoker set search_path = '' as $$
declare
 d uuid; u jsonb; sess public.fredi_sessions; actor integer; s text; rev bigint;
 req uuid; previous public.fredi_sync_batches; rec jsonb; body jsonb; cid integer;
 cnt integer; limit_key text; user_version bigint;
begin
 if op = 'login_lookup' then
  d := (args->>'dataset')::uuid;
  -- Limits by user and by source address, including nonexistent names. No enumeration response.
  foreach limit_key in array array[d::text || ':user:' || lower(args->>'username'), 'ip:' || (args->>'ip')] loop
   insert into public.fredi_login_limits(key,attempts) values(limit_key,1)
   on conflict(key) do update set attempts = case when fredi_login_limits.starts_at < now()-interval '15 minutes' then 1 else fredi_login_limits.attempts+1 end,
    starts_at = case when fredi_login_limits.starts_at < now()-interval '15 minutes' then now() else fredi_login_limits.starts_at end
   returning attempts into cnt;
   if cnt > (case when limit_key like 'ip:%' then 150 else 15 end) then return jsonb_build_object('error','rate_limit'); end if;
  end loop;
  select payload into u from public.fredi_users where dataset=d and username=lower(args->>'username');
  if u is null or (u->>'Disabled')::boolean then return jsonb_build_object('error','unauthorized'); end if;
  return jsonb_build_object('user',u);
 end if;
 if op = 'login_finish' then
  d := (args->>'dataset')::uuid;
  select payload into u from public.fredi_users where dataset=d and id=(args->>'userId')::integer;
  if u is null or (u->>'Disabled')::boolean or (u->>'SecurityVersion')::bigint <> (args->>'version')::bigint
   then return jsonb_build_object('error','unauthorized'); end if;
  delete from public.fredi_sessions where expires_at < now();
  delete from public.fredi_login_limits where starts_at < now()-interval '1 day';
  insert into public.fredi_sessions(token_hash,dataset,user_id,security_version,expires_at)
   values(args->>'tokenHash',d,(u->>'Id')::integer,(u->>'SecurityVersion')::bigint,now()+interval '1 hour');
  return jsonb_build_object('ok',true);
 end if;
 select * into sess from public.fredi_sessions where token_hash=args->>'tokenHash' for update;
 if not found or sess.expires_at < now() or sess.touched_at < now()-interval '15 minutes'
  then return jsonb_build_object('error','unauthorized'); end if;
 d := sess.dataset; actor := sess.user_id;
 select payload into u from public.fredi_users where dataset=d and id=actor;
 if u is null or (u->>'Disabled')::boolean or (u->>'SecurityVersion')::bigint <> sess.security_version
  then return jsonb_build_object('error','unauthorized'); end if;
 update public.fredi_sessions set touched_at=now() where token_hash=sess.token_hash;
 if op='logout' then
  delete from public.fredi_sessions where token_hash=sess.token_hash;
  return jsonb_build_object('ok',true);
 end if;
 if op='status' then
  return jsonb_build_object('scopes',coalesce((select jsonb_agg(jsonb_build_object('scope',scope,'revision',revision,'updatedAt',updated_at))
    from public.fredi_scopes where dataset=d and ((u->>'IsMaster')::boolean or scope='company:'||(u->>'CompanyId') or scope='draft:'||actor::text)), '[]'::jsonb));
 end if;
 if op='companies' then
  return jsonb_build_object('companies',coalesce((select jsonb_agg(payload) from public.fredi_records where dataset=d and scope='admin' and entity='Company'
   and ((u->>'IsMaster')::boolean or local_key=u->>'CompanyId')), '[]'::jsonb));
 end if;
 if op='catalogs' then
  if not (u->>'IsMaster')::boolean then return jsonb_build_object('error','forbidden'); end if;
  return jsonb_build_object('catalogs',coalesce((select jsonb_agg(payload) from public.fredi_records where dataset=d and scope='admin' and entity='CatalogPackage'), '[]'::jsonb));
 end if;
 if op='company' then
  cid := (args->>'companyId')::integer;
  if not (u->>'IsMaster')::boolean and cid <> (u->>'CompanyId')::integer then return jsonb_build_object('error','forbidden'); end if;
  s := 'company:'||cid::text;
  return jsonb_build_object('revision',(select revision from public.fredi_scopes where dataset=d and scope=s),
   'records',coalesce((select jsonb_agg(jsonb_build_object('Table',entity,'Key',local_key,'Data',payload)) from public.fredi_records where dataset=d and scope=s), '[]'::jsonb));
 end if;
 if op <> 'sync' then return jsonb_build_object('error','not_found'); end if;
 body := args->'body'; s := body->>'Scope'; req := (body->>'RequestId')::uuid;
 if (body->>'Dataset')::uuid is distinct from d then return jsonb_build_object('error','forbidden'); end if;
 if not (u->>'IsMaster')::boolean and s <> 'company:'||(u->>'CompanyId') and s <> 'draft:'||actor::text
  then return jsonb_build_object('error','forbidden'); end if;
 if s !~ '^(admin|company:[1-9][0-9]*|draft:[1-9][0-9]*)$' then return jsonb_build_object('error','invalid_scope'); end if;
 insert into public.fredi_scopes(dataset,scope) values(d,s) on conflict do nothing;
 select revision into rev from public.fredi_scopes where dataset=d and scope=s for update;
 select * into previous from public.fredi_sync_batches where dataset=d and request_id=req;
 if found then
  if previous.payload <> body then return jsonb_build_object('error','conflict'); end if;
  return jsonb_build_object('requestId',req,'revision',previous.revision);
 end if;
 if rev is distinct from (body->>'BaseRevision')::bigint then return jsonb_build_object('error','conflict'); end if;
 if jsonb_typeof(body->'Records') is distinct from 'array' or jsonb_array_length(body->'Records')>100000 then raise exception 'invalid records'; end if;
 if s like 'company:%' then
  cid := substring(s from 9)::integer;
  if not exists(select 1 from public.fredi_records where dataset=d and scope='admin' and entity='Company' and local_key=cid::text)
   then return jsonb_build_object('error','missing_company'); end if;
 end if;
 for rec in select value from jsonb_array_elements(body->'Records') loop
  if jsonb_typeof(rec->'Data') is distinct from 'object' or length(rec->>'Key') not between 1 and 200 then raise exception 'invalid record'; end if;
  if s='admin' then
   if rec->>'Table' not in ('Company','CatalogPackage','LocalUser') then raise exception 'invalid admin entity'; end if;
  elsif s like 'draft:%' then
   if rec->>'Table' is distinct from 'JournalDraft' or (rec->'Data'->>'UserId')::integer is distinct from substring(s from 7)::integer then raise exception 'invalid draft owner'; end if;
   if not (u->>'IsMaster')::boolean and (rec->'Data'->>'CompanyId')::integer is distinct from (u->>'CompanyId')::integer then raise exception 'invalid draft company'; end if;
  else
   if rec->>'Table' not in ('Account','PolicyType','Journal','JournalLine','ImportedBalance','MigrationPolicy','ImportBatch','DetailedPeriod') then raise exception 'invalid company entity'; end if;
   if rec->>'Table'<>'JournalLine' and (rec->'Data'->>'CompanyId')::integer is distinct from cid then raise exception 'invalid company'; end if;
  end if;
  if rec->>'Key' is distinct from coalesce(rec->'Data'->>'Id',rec->'Data'->>'Key',rec->'Data'->>'CompanyId') then raise exception 'invalid identity'; end if;
 end loop;
 -- Validate the complete new snapshot before replacing the previous revision.
 if s like 'company:%' then
  if exists(select 1 from jsonb_array_elements(body->'Records') l where l->>'Table'='JournalLine' and
   (not exists(select 1 from jsonb_array_elements(body->'Records') j where j->>'Table'='Journal' and j->>'Key'=l->'Data'->>'JournalId') or
    not exists(select 1 from jsonb_array_elements(body->'Records') a where a->>'Table'='Account' and a->>'Key'=l->'Data'->>'AccountId') or
    (l->'Data'->>'DebitCents')::bigint < 0 or (l->'Data'->>'CreditCents')::bigint < 0)) then raise exception 'invalid journal lines'; end if;
  if exists(select 1 from jsonb_array_elements(body->'Records') l where l->>'Table'='JournalLine'
   group by l->'Data'->>'JournalId' having sum((l->'Data'->>'DebitCents')::numeric) <> sum((l->'Data'->>'CreditCents')::numeric)) then raise exception 'unbalanced journal'; end if;
 end if;
 if s='admin' then
  if not exists(select 1 from jsonb_array_elements(body->'Records') r where r->>'Table'='LocalUser' and (r->'Data'->>'IsMaster')::boolean and not (r->'Data'->>'Disabled')::boolean) then raise exception 'active master required'; end if;
  delete from public.fredi_users where dataset=d;
  insert into public.fredi_users(dataset,id,username,payload)
   select d,(r->'Data'->>'Id')::integer,r->'Data'->>'Username',r->'Data' from jsonb_array_elements(body->'Records') r where r->>'Table'='LocalUser';
  delete from public.fredi_sessions ss where ss.dataset=d and not exists(select 1 from public.fredi_users uu where uu.dataset=d and uu.id=ss.user_id and not (uu.payload->>'Disabled')::boolean and (uu.payload->>'SecurityVersion')::bigint=ss.security_version);
 end if;
 delete from public.fredi_records where dataset=d and scope=s;
 insert into public.fredi_records(dataset,scope,entity,local_key,payload)
  select d,s,r->>'Table',r->>'Key',r->'Data' from jsonb_array_elements(body->'Records') r;
 rev := rev+1;
 update public.fredi_scopes set revision=rev,updated_at=now() where dataset=d and scope=s;
 insert into public.fredi_sync_batches(dataset,request_id,scope,revision,user_id,payload) values(d,req,s,rev,actor,body);
 return jsonb_build_object('requestId',req,'revision',rev);
end;
$$;
revoke all on function public.fredi_gateway(text,jsonb) from public,anon,authenticated;
grant execute on function public.fredi_gateway(text,jsonb) to service_role;
