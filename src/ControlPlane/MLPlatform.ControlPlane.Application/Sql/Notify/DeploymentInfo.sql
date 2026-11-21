-- mlp:query DeploymentInfo :one
SELECT id, workspace_id, created_by, slug FROM serving.deployments WHERE id = @id OR job_id = @id LIMIT 1;
