-- mlp:query RunInfoByJob :one
SELECT id, workspace_id, user_id, project_id, name FROM track.runs WHERE job_id = @job_id;
