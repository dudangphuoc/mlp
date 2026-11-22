-- mlp:query MarkGpuAlertNotified :exec
UPDATE ops.gpu_alerts SET notified_at = @now WHERE host_id = @host_id AND gpu_uuid = @gpu_uuid AND kind = @kind;
