-- mlp:query HostLastMetrics :one
SELECT last_metrics::text AS metrics, metrics_at FROM fleet.hosts WHERE id = @id AND workspace_id = @workspace_id;
