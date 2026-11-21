-- mlp:query InsertRun :exec
INSERT INTO track.runs (id, project_id, workspace_id, name, kind, state, job_id, user_id, recipe, variant, task, params,
                        primary_metric_key, created_at, number, dataset_version_id, from_scratch, gpus, cpus)
VALUES (@id, @project_id, @workspace_id, @name, 'train', 'queued', @job_id, @user_id, @recipe, @variant, @task, @params_json::jsonb,
        @primary_metric_key, @now, @number, @dataset_version_id, @from_scratch, @gpus, @cpus);
