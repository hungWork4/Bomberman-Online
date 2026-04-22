-- Atomic purchase RPC for store flow.
-- Run in Supabase SQL Editor after your profile setup.

create or replace function public.purchase_skin_atomic(p_skin_id bigint)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_user_id uuid := auth.uid();
  v_price bigint;
  v_gold bigint;
  v_owned boolean;
begin
  if v_user_id is null then
    return jsonb_build_object(
      'success', false,
      'message', 'UNAUTHORIZED',
      'remaining_gold', 0,
      'owned_skin_id', p_skin_id
    );
  end if;

  select s.price into v_price
  from public.skins s
  where s.id = p_skin_id;

  if v_price is null then
    return jsonb_build_object(
      'success', false,
      'message', 'SKIN_NOT_FOUND',
      'remaining_gold', 0,
      'owned_skin_id', p_skin_id
    );
  end if;

  select exists(
    select 1
    from public.player_inventory pi
    where pi.player_id = v_user_id
      and pi.skin_id = p_skin_id
  ) into v_owned;

  if v_owned then
    select coalesce(ps.gold, 0) into v_gold
    from public.player_stats ps
    where ps.id = v_user_id;

    return jsonb_build_object(
      'success', true,
      'message', 'ALREADY_OWNED',
      'remaining_gold', coalesce(v_gold, 0),
      'owned_skin_id', p_skin_id
    );
  end if;

  -- Lock player_stats row to avoid double spending in concurrent requests.
  select coalesce(ps.gold, 0) into v_gold
  from public.player_stats ps
  where ps.id = v_user_id
  for update;

  if v_gold < v_price then
    return jsonb_build_object(
      'success', false,
      'message', 'NOT_ENOUGH_GOLD',
      'remaining_gold', coalesce(v_gold, 0),
      'owned_skin_id', p_skin_id
    );
  end if;

  update public.player_stats
  set gold = gold - v_price
  where id = v_user_id;

  insert into public.player_inventory (player_id, skin_id, purchased_at)
  values (v_user_id, p_skin_id, now())
  on conflict do nothing;

  select coalesce(ps.gold, 0) into v_gold
  from public.player_stats ps
  where ps.id = v_user_id;

  return jsonb_build_object(
    'success', true,
    'message', 'PURCHASED',
    'remaining_gold', coalesce(v_gold, 0),
    'owned_skin_id', p_skin_id
  );
end;
$$;

revoke all on function public.purchase_skin_atomic(bigint) from public;
grant execute on function public.purchase_skin_atomic(bigint) to authenticated;

-- Recommended constraints/indexes for consistency and speed.
create unique index if not exists player_inventory_player_skin_key
  on public.player_inventory(player_id, skin_id);

create index if not exists player_inventory_player_id_idx
  on public.player_inventory(player_id);

create index if not exists player_stats_id_idx
  on public.player_stats(id);
