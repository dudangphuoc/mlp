-- mlp:query DeleteGpuAlert :exec
DELETE FROM ops.gpu_alerts WHERE host_id = @host_id AND gpu_uuid = @gpu_uuid AND kind = @kind;
