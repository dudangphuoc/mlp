-- mlp:query InsertJob :exec
INSERT INTO sched.jobs (id, workspace_id, kind, name, owner_id, spec, priority, state, created_at, queued_at)
VALUES (@id, @workspace_id, @kind, @name, @owner_id, @spec::jsonb, @priority, 'queued', @now, @now);
