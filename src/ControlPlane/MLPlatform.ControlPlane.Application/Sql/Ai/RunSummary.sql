-- mlp:query RunSummary :one
-- mlp:no-workspace filtered by workspace and/or instance-level runs, decided by the caller's rights
-- Header indicator: what the AI is doing now and what went wrong recently.
SELECT count(*) FILTER (WHERE state = 'running') AS running,
       count(*) FILTER (WHERE state = 'failed' AND started_at > @since) AS failed_recent,
       count(*) FILTER (WHERE fallback IS NOT NULL AND started_at > @since) AS fallback_recent,
       max(started_at) AS last_at
FROM ai.runs WHERE (@workspace_id::uuid IS NOT NULL AND workspace_id = @workspace_id) OR (@include_instance AND workspace_id IS NULL);
