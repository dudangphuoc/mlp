-- mlp:query DeleteCheckpoint :exec
DELETE FROM sched.checkpoints WHERE id = @id;
