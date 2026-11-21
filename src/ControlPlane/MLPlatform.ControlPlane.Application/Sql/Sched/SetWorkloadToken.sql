-- mlp:query SetWorkloadToken :exec
UPDATE sched.job_attempts SET workload_token_hash = @token_hash WHERE id = @id;
