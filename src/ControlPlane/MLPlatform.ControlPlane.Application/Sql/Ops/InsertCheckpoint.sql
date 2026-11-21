-- mlp:query InsertCheckpoint :exec
INSERT INTO sched.checkpoints (id, job_id, attempt_id, host_id, step, name, location, s3_key, size_bytes, sha256, created_at)
VALUES (@id, @job_id, @attempt_id, @host_id, @step, @name, @location, @s3_key, @size_bytes, @sha256, @now)
ON CONFLICT (job_id, name, location) DO UPDATE SET attempt_id = excluded.attempt_id, host_id = excluded.host_id, step = excluded.step,
  s3_key = excluded.s3_key, size_bytes = excluded.size_bytes, sha256 = excluded.sha256, created_at = excluded.created_at;
