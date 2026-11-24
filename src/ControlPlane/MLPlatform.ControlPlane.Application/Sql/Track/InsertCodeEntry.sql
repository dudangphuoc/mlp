-- mlp:query InsertCodeEntry :exec
INSERT INTO track.code_entries (id, workspace_id, project_id, name, git_remote, ref, script, machine, gpus, image, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @name, @git_remote, @default_ref, @script, @machine, @gpus, @image, @created_by, @now);
