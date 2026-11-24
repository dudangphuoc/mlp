-- mlp:query GetCodeEntry :one
SELECT id, workspace_id, project_id, name, git_remote, ref, script, machine, gpus, image, created_at
FROM track.code_entries WHERE id = @id AND workspace_id = @workspace_id;
