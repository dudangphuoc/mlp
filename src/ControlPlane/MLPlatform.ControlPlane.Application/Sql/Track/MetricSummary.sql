-- mlp:query MetricSummary :many
-- One row per metric: enough to list every metric of a run without loading its points.
SELECT key, count(*) AS points, max(step) AS last_step, min(value) AS min_value, max(value) AS max_value,
       (array_agg(value ORDER BY step DESC))[1] AS last_value
FROM track.metrics WHERE run_id = @run_id GROUP BY key ORDER BY key;
