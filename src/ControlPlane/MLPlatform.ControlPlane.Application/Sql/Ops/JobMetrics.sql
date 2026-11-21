-- mlp:query JobMetrics :many
SELECT m.attempt_id, m.at, m.cpu_percent, m.memory_bytes, m.gpu_util, m.vram_bytes
FROM ops.attempt_metrics m JOIN sched.job_attempts a ON a.id = m.attempt_id
WHERE a.job_id = @job_id AND m.at > @since ORDER BY m.at;
