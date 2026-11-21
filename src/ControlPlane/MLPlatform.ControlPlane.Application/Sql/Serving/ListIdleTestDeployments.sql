-- mlp:query ListIdleTestDeployments :many
SELECT id, workspace_id, slug, model_version_id, model_name, model_version, task, state, state_reason, job_id, endpoint, created_at, purpose, idle_stop_min, last_used_at, cpus, gpus, image_override
FROM serving.deployments
WHERE idle_stop_min IS NOT NULL AND state <> 'stopped'
  AND coalesce(last_used_at, created_at) + make_interval(mins => idle_stop_min) < @now;
