-- mlp:query InsertAttemptMetric :exec
INSERT INTO ops.attempt_metrics (attempt_id, at, cpu_percent, memory_bytes, gpu_util, vram_bytes)
SELECT @attempt_id, @now, @cpu_percent, @memory_bytes, @gpu_util, @vram_bytes
WHERE EXISTS (SELECT 1 FROM sched.job_attempts WHERE id = @attempt_id);
