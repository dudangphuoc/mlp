-- mlp:query JobsByIds :many
-- State, reason and progress of several jobs at once (run list: one query for the whole page).
SELECT id, state, state_reason, started_at, attempt_count, progress::text AS progress, progress_at
FROM sched.jobs WHERE workspace_id = @workspace_id AND id = ANY(@ids);
