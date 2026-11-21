-- mlp:query InsertServer :exec
INSERT INTO nb.servers (id, workspace_id, user_id, name, profile, image, ide, cpus, memory_gb, gpus, job_id, state, idle_timeout_min, last_activity_at, created_at)
VALUES (@id, @workspace_id, @user_id, @name, @profile, @image, @ide, @cpus, @memory_gb, @gpus, @job_id, 'queued', @idle_timeout_min, @now, @now);
