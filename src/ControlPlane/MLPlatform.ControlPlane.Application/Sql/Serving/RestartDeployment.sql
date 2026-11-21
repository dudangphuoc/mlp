-- mlp:query RestartDeployment :exec
-- A stopped deployment started again keeps its id, keys and public address; it gets a new job and possibly another model version.
UPDATE serving.deployments
SET model_version_id = @model_version_id, model_name = @model_name, model_version = @model_version, task = @task, state = 'deploying',
    state_reason = NULL, endpoint = NULL, job_id = @job_id, purpose = @purpose, idle_stop_min = @idle_stop_min,
    cpus = @cpus, gpus = @gpus, image_override = @image_override, last_used_at = NULL
WHERE id = @id AND workspace_id = @workspace_id AND state IN ('stopped', 'failed');
