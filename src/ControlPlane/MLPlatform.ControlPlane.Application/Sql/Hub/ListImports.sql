-- mlp:query ListImports :many
SELECT id, kind, repo, revision, target_id, target_name, task, state, files_total, files_done, bytes_total, bytes_done, message, created_at, finished_at, source, mode
FROM hub.imports WHERE workspace_id = @workspace_id ORDER BY created_at DESC LIMIT 50;
