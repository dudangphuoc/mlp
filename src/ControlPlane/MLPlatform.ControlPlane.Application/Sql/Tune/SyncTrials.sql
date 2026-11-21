-- mlp:query SyncTrials :exec
-- State + objective value taken from the M09 run (objective metric = last value of the objective key; the primary metric uses primary_metric_value directly).
UPDATE tune.trials t SET
  state = CASE WHEN r.state IN ('succeeded', 'failed', 'cancelled', 'running', 'queued') THEN r.state ELSE t.state END,
  value = coalesce((SELECT m.value FROM track.metrics m WHERE m.run_id = r.id AND m.key = @objective ORDER BY m.step DESC, m.at DESC LIMIT 1),
                   CASE WHEN r.primary_metric_key = @objective THEN r.primary_metric_value END)
FROM track.runs r
WHERE t.sweep_id = @sweep_id AND t.run_id = r.id AND t.state IN ('queued', 'running');
