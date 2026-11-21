-- mlp:query TouchServer :exec
UPDATE nb.servers SET last_activity_at = @now, idle_warned_at = NULL WHERE id = @id AND state = 'running';
