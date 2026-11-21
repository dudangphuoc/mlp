-- mlp:query GetImport :one
-- mlp:no-workspace internal worker reads by job id (created within a workspace)
SELECT id, workspace_id, kind, repo, revision, include, target_id, target_name, task, state, created_by, source, mode FROM hub.imports WHERE id = @id;
