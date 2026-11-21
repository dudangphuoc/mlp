-- mlp:query SetHostWgKey :exec
-- mlp:no-workspace agent channel has authenticated the machine by credential; machine id is the unique key
UPDATE fleet.hosts SET wg_public_key = @wg_public_key WHERE id = @id AND wg_public_key IS DISTINCT FROM @wg_public_key;
