-- mlp:query SetHostState :exec
UPDATE fleet.hosts SET state = @state, state_reason = @reason, last_seen_at = @now WHERE id = @id;
