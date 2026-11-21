-- mlp:query FinalMetrics :many
SELECT DISTINCT ON (key) key, value FROM track.metrics WHERE run_id = @run_id ORDER BY key, step DESC;
