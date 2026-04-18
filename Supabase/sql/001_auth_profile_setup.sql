-- Supabase auth/profile setup for username-or-email login.
-- Run this script in Supabase SQL Editor.

-- 1) Core profile table mapped 1:1 with auth.users
create table if not exists public.player_profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  username text not null unique,
  email text,
  display_name text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

-- If the table already existed before this migration, ensure required columns are present.
alter table public.player_profiles add column if not exists username text;
alter table public.player_profiles add column if not exists email text;
alter table public.player_profiles add column if not exists display_name text;
alter table public.player_profiles add column if not exists created_at timestamptz not null default now();
alter table public.player_profiles add column if not exists updated_at timestamptz not null default now();

-- Ensure id is not auto-generated. It must match auth.users.id.
alter table public.player_profiles
  alter column id drop default;

-- Helpful index for case-insensitive username lookup.
create index if not exists player_profiles_username_lower_idx
  on public.player_profiles (lower(username));

-- Ensure uniqueness even when the table was created previously without constraints.
create unique index if not exists player_profiles_username_key
  on public.player_profiles (username);

-- 2) Keep updated_at fresh
create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists trg_player_profiles_set_updated_at on public.player_profiles;
create trigger trg_player_profiles_set_updated_at
before update on public.player_profiles
for each row execute function public.set_updated_at();

-- 3) Auto-create profile when a new auth user is created
create or replace function public.handle_new_auth_user_profile()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
  insert into public.player_profiles (id, username, email, display_name)
  values (
    new.id,
    coalesce(new.raw_user_meta_data ->> 'username', split_part(new.email, '@', 1)),
    new.email,
    coalesce(new.raw_user_meta_data ->> 'display_name', '')
  )
  on conflict (id) do update
    set username = excluded.username,
        email = excluded.email,
        display_name = excluded.display_name,
        updated_at = now();

  return new;
end;
$$;

drop trigger if exists on_auth_user_created_profile on auth.users;
create trigger on_auth_user_created_profile
after insert on auth.users
for each row execute function public.handle_new_auth_user_profile();

-- 4) Backfill old users into player_profiles
insert into public.player_profiles (id, username, email, display_name)
select
  u.id,
  coalesce(u.raw_user_meta_data ->> 'username', split_part(u.email, '@', 1)) as username,
  u.email,
  coalesce(u.raw_user_meta_data ->> 'display_name', '') as display_name
from auth.users u
on conflict (id) do update
  set username = excluded.username,
      email = excluded.email,
      display_name = excluded.display_name,
      updated_at = now();

-- 5) RLS policies: users can read/update only their own profile
alter table public.player_profiles enable row level security;

drop policy if exists player_profiles_select_own on public.player_profiles;
create policy player_profiles_select_own
on public.player_profiles
for select
to authenticated
using (auth.uid() = id);

drop policy if exists player_profiles_update_own on public.player_profiles;
create policy player_profiles_update_own
on public.player_profiles
for update
to authenticated
using (auth.uid() = id)
with check (auth.uid() = id);

drop policy if exists player_profiles_insert_own on public.player_profiles;
create policy player_profiles_insert_own
on public.player_profiles
for insert
to authenticated
with check (auth.uid() = id);

-- 6) RPC used by Unity login to resolve username -> email
create or replace function public.resolve_login_email(p_username text)
returns table(email text)
language sql
security definer
set search_path = public
as $$
  select p.email
  from public.player_profiles p
  where lower(p.username) = lower(p_username)
  limit 1;
$$;

revoke all on function public.resolve_login_email(text) from public;
grant execute on function public.resolve_login_email(text) to anon, authenticated;

-- 7) Optional: ask PostgREST to reload schema cache immediately
select pg_notify('pgrst', 'reload schema');
