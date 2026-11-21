-- mlp:query ProjectStats :many
-- Per project: how many runs, how many still active, the last activity and the best result so far.
-- "Best" follows the metric's direction: loss-like metrics improve downwards, everything else upwards.
SELECT p.id AS project_id,
       (SELECT count(*) FROM track.runs r WHERE r.project_id = p.id AND NOT r.is_trial) AS run_count,
       (SELECT count(*) FROM track.runs r WHERE r.project_id = p.id AND NOT r.is_trial AND r.state NOT IN ('succeeded', 'failed', 'cancelled')) AS active_count,
       (SELECT max(r.created_at) FROM track.runs r WHERE r.project_id = p.id AND NOT r.is_trial) AS last_run_at,
       b.number AS best_number, b.name AS best_name, b.primary_metric_key AS best_metric_key, b.primary_metric_value AS best_metric_value
FROM track.projects p
LEFT JOIN LATERAL (
  SELECT r.number, r.name, r.primary_metric_key, r.primary_metric_value FROM track.runs r
  WHERE r.project_id = p.id AND NOT r.is_trial AND r.primary_metric_value IS NOT NULL
  ORDER BY CASE WHEN r.primary_metric_key ~* '(loss|error|wer|cer|rmse|mae|mse|perplexity)' THEN r.primary_metric_value ELSE -r.primary_metric_value END
  LIMIT 1) b ON true
WHERE p.workspace_id = @workspace_id;
