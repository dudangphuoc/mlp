-- mlp:query GetDeployment :one
SELECT id, workspace_id, slug, model_version_id, model_name, model_version, task, state, state_reason, job_id, endpoint, created_at, purpose, idle_stop_min, last_used_at, cpus, gpus, image_override
FROM serving.deployments WHERE id = @id AND workspace_id = @workspace_id;
