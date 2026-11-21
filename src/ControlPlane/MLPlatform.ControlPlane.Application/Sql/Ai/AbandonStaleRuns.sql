-- mlp:query AbandonStaleRuns :exec
-- mlp:no-workspace startup cleanup across all runs: a run still "running" after a restart can never finish
UPDATE ai.runs SET state = 'failed', error_code = 'ABANDONED', error = 'The server restarted while this call was running', finished_at = @now
WHERE state = 'running' AND started_at < @before;
