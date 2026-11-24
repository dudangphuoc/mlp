-- mlp:query ListCodeEntries :many
SELECT id, workspace_id, project_id, name, git_remote, ref, script, machine, gpus, image, created_at
FROM track.code_entries WHERE workspace_id = @workspace_id AND project_id = @project_id ORDER BY name;
