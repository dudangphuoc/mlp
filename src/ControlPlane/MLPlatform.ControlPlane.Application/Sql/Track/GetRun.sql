-- mlp:query GetRun :one
SELECT id, project_id, workspace_id, name, state, recipe, variant, task, job_id, params::text AS params, primary_metric_key,
       primary_metric_value, created_at, number, dataset_version_id, from_scratch, gpus, cpus, ended_at, user_id
FROM track.runs WHERE id = @id;
