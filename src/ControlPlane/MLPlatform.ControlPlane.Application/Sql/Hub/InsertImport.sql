-- mlp:query InsertImport :exec
INSERT INTO hub.imports (id, workspace_id, kind, repo, revision, include, target_id, target_name, task, state, created_by, created_at, updated_at, source, mode)
VALUES (@id, @workspace_id, @kind, @repo, @revision, @include, @target_id, @target_name, @task, 'queued', @created_by, @now, @now, @source, @mode);
