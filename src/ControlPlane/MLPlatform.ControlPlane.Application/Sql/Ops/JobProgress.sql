-- mlp:query JobProgress :one
SELECT progress::text AS progress, progress_at FROM sched.jobs WHERE id = @id AND workspace_id = @workspace_id;
