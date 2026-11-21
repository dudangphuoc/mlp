-- mlp:query JobWorkspace :scalar
SELECT workspace_id FROM sched.jobs WHERE id = @id;
