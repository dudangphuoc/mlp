-- mlp:query InsertRemoteCodeRun :exec
INSERT INTO track.runs (id, project_id, workspace_id, name, kind, state, job_id, user_id, recipe, variant, task, params,
                        primary_metric_key, created_at, number, gpus, cpus, origin, host_name, code)
VALUES (@id, @project_id, @workspace_id, @name, 'train', 'queued', @job_id, @user_id, '', '', @task, @params_json::jsonb,
        '', @now, @number, @gpus, 1, 'code', @host_name, @code_json::jsonb);
