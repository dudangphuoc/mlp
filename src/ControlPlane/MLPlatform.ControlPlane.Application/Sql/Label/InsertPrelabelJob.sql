-- mlp:query InsertPrelabelJob :exec
INSERT INTO label.prelabel_jobs (id, workspace_id, project_id, source_kind, source_id, source_label, total, state, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @source_kind, @source_id, @source_label, @total, 'queued', @created_by, @now);
