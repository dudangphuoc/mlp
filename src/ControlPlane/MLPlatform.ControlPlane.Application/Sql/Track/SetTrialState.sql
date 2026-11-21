-- mlp:query SetTrialState :exec
UPDATE track.trials SET state = @state, error = @error, finished_at = CASE WHEN @state::text IN ('queued', 'running') THEN NULL ELSE @now::timestamptz END
WHERE id = @id AND workspace_id = @workspace_id AND state IN ('queued', 'running');
