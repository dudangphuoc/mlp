-- mlp:query GetMetricsSampled :many
-- At most @max_points per metric: steps are grouped into equal buckets and averaged, so a 100k-step loss curve stays light.
WITH b AS (
  SELECT key, min(step) AS lo, max(step) AS hi FROM track.metrics
  WHERE run_id = @run_id AND (NOT @use_keys OR key = ANY(@keys)) GROUP BY key)
SELECT m.key, max(m.step) AS step, max(m.at) AS at, avg(m.value) AS value
FROM track.metrics m JOIN b ON b.key = m.key
WHERE m.run_id = @run_id
GROUP BY m.key, CASE WHEN b.hi - b.lo < @max_points THEN m.step - b.lo ELSE (m.step - b.lo) * @max_points / (b.hi - b.lo + 1) END
ORDER BY m.key, step;
