-- mlp:query RollupGpuSamples :exec
INSERT INTO ops.gpu_samples_1m (host_id, gpu_uuid, at, util, vram_used, vram_total, temp, power, sm_clock)
SELECT host_id, gpu_uuid, date_trunc('minute', at), avg(util), max(vram_used), max(vram_total), max(temp), avg(power), avg(sm_clock)
FROM ops.gpu_samples WHERE at >= @from AND at < @to
GROUP BY host_id, gpu_uuid, date_trunc('minute', at)
ON CONFLICT DO NOTHING;
