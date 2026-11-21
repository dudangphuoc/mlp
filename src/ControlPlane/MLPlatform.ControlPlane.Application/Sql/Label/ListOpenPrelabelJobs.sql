-- mlp:query ListOpenPrelabelJobs :many
-- mlp:no-workspace background runner picks up queued/running jobs of every workspace
SELECT j.id, j.project_id, j.source_kind, j.source_id, j.source_label, j.total, j.done, j.failed, j.state, j.error, coalesce(nullif(u.display_name, ''), u.username) AS created_by_name, j.created_by, j.created_at, j.started_at, j.finished_at, j.workspace_id
FROM label.prelabel_jobs j LEFT JOIN identity.users u ON u.id = j.created_by
WHERE j.state IN ('queued', 'running') ORDER BY j.created_at LIMIT 10;
