-- mlp:query ListGpuAlerts :many
SELECT host_id, gpu_uuid, kind, level, since, notified_at FROM ops.gpu_alerts WHERE host_id = @host_id ORDER BY since;
