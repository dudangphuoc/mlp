-- mlp:query GetJobByWorkloadToken :scalar
SELECT job_id FROM sched.job_attempts WHERE workload_token_hash = @token_hash AND state IN ('launching','running','suspect');
