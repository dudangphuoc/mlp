-- mlp:query GetMetrics :many
SELECT key, step, at, value FROM track.metrics WHERE run_id = @run_id ORDER BY key, step;
