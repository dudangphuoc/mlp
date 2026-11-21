-- mlp:query InsertDeployment :exec
INSERT INTO serving.deployments (id, workspace_id, slug, model_version_id, model_name, model_version, task, state, job_id, created_by, created_at, purpose, idle_stop_min,
                                 cpus, gpus, image_override)
VALUES (@id, @workspace_id, @slug, @model_version_id, @model_name, @model_version, @task, 'deploying', @job_id, @created_by, @now, @purpose, @idle_stop_min,
        @cpus, @gpus, @image_override);
