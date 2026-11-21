-- mlp:query Stalled :many
-- Progress unchanged for longer than `stall_after_min` (recipe; default 30) — "may be stalled" (tier 3).
SELECT j.id, j.workspace_id, j.owner_id, j.name, (j.progress->>'current')::double precision AS current
FROM sched.jobs j
WHERE j.state = 'running' AND j.stalled_at IS NULL AND j.progress_changed_at IS NOT NULL
  AND j.progress_changed_at + make_interval(mins => coalesce((j.progress->>'stall_after_min')::int, 30)) < @now;
