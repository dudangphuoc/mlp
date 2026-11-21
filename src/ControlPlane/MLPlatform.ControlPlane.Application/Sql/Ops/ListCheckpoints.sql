-- mlp:query ListCheckpoints :many
SELECT id, attempt_id, host_id, step, name, location, s3_key, size_bytes, sha256, created_at
FROM sched.checkpoints WHERE job_id = @job_id ORDER BY step DESC, location;
