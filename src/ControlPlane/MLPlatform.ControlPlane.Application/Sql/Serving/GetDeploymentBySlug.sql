-- mlp:query GetDeploymentBySlug :one
SELECT id, workspace_id, slug, model_version_id, model_name, model_version, task, state, state_reason, job_id, endpoint, created_at, purpose, idle_stop_min, last_used_at, cpus, gpus, image_override
FROM serving.deployments WHERE workspace_id = @workspace_id AND slug = @slug;
