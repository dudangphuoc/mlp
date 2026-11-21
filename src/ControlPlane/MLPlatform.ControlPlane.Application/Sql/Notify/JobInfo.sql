-- mlp:query JobInfo :one
SELECT id, workspace_id, owner_id, kind, name, started_at, finished_at FROM sched.jobs WHERE id = @id;
