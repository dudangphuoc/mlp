-- mlp:query RearmServers :exec
UPDATE nb.servers SET last_activity_at = @now, idle_warned_at = NULL WHERE state = 'running';
