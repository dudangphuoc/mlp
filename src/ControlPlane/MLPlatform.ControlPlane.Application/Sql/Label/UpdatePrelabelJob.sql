-- mlp:query UpdatePrelabelJob :exec
-- mlp:no-workspace background runner updates the job it owns (id from ListOpenPrelabelJobs)
UPDATE label.prelabel_jobs SET done = @done, failed = @failed, state = @state, error = @error,
    started_at = coalesce(started_at, @now), finished_at = CASE WHEN @state IN ('succeeded', 'failed', 'cancelled') THEN @now ELSE NULL END
WHERE id = @id AND state <> 'cancelled';
