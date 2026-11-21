-- mlp:query FindActiveForVersion :one
-- Reuse a running replica of the same model version for the playground: healthy first, then still starting.
SELECT id, workspace_id, slug, model_version_id, model_name, model_version, task, state, state_reason, job_id, endpoint, created_at, purpose, idle_stop_min, last_used_at, cpus, gpus, image_override
FROM serving.deployments
WHERE workspace_id = @workspace_id AND model_version_id = @model_version_id AND state IN ('deploying', 'healthy', 'degraded')
ORDER BY CASE state WHEN 'healthy' THEN 0 WHEN 'deploying' THEN 1 ELSE 2 END, created_at DESC
LIMIT 1;
