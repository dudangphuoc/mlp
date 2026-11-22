-- mlp:query UpsertGpuAlert :exec
INSERT INTO ops.gpu_alerts (host_id, gpu_uuid, kind, level, since) VALUES (@host_id, @gpu_uuid, @kind, @level, @now)
ON CONFLICT (host_id, gpu_uuid, kind) DO UPDATE SET level = EXCLUDED.level;
