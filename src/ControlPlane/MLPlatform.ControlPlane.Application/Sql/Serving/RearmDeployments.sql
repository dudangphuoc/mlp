-- mlp:query RearmDeployments :exec
UPDATE serving.deployments SET last_used_at = @now WHERE idle_stop_min IS NOT NULL AND state <> 'stopped';
