-- mlp:query InsertGpuSample :exec
INSERT INTO ops.gpu_samples (host_id, gpu_uuid, at, util, mem_util, vram_used, vram_total, temp, power, power_limit, sm_clock, throttle, ecc)
VALUES (@host_id, @gpu_uuid, @at, @util, @mem_util, @vram_used, @vram_total, @temp, @power, @power_limit, @sm_clock, @throttle, @ecc)
ON CONFLICT DO NOTHING;
