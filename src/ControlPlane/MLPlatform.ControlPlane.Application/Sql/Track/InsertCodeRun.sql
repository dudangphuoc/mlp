-- mlp:query InsertCodeRun :exec
INSERT INTO track.runs (id, project_id, workspace_id, name, kind, state, job_id, user_id, recipe, variant, task, params,
                        primary_metric_key, created_at, number, gpus, cpus, origin, host_name, code, heartbeat_at)
VALUES (@id, @project_id, @workspace_id, @name, 'train', 'running', NULL, @user_id, '', '', @task, @params_json::jsonb,
        '', @now, @number, 0, 0, 'code', @host_name, @code_json::jsonb, @now);
