-- mlp:query SetHostMetrics :exec
UPDATE fleet.hosts SET last_metrics = @metrics::jsonb, metrics_at = @now WHERE id = @id;
