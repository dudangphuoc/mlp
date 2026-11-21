-- mlp:query InsertMetric :exec
INSERT INTO track.metrics (run_id, key, step, at, value) VALUES (@run_id, @key, @step, @at, @value)
ON CONFLICT (run_id, key, step) DO UPDATE SET value = excluded.value, at = excluded.at;
