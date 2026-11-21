-- mlp:query RestartServer :exec
UPDATE nb.servers SET job_id = @job_id, image = @image, host_id = @host_id, state = 'queued', state_reason = NULL, endpoint = NULL,
    started_at = NULL, stopped_at = NULL, idle_warned_at = NULL, last_activity_at = @now
WHERE id = @id AND workspace_id = @workspace_id;
