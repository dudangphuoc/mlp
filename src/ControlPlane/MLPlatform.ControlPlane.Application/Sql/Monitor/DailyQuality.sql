-- mlp:query DailyQuality :many
-- Actual daily quality from labeled predictions (classification: accuracy; regression: MAE).
SELECT date_trunc('day', at) AS day, count(*) AS labeled,
  avg(CASE WHEN ground_truth ? 'label' THEN (CASE WHEN prediction->>'label' = ground_truth->>'label' THEN 1.0 ELSE 0.0 END) END)::double precision AS accuracy,
  avg(CASE WHEN ground_truth ? 'value' THEN abs((prediction->>'value')::double precision - (ground_truth->>'value')::double precision) END)::double precision AS mae
FROM monitor.predictions WHERE deployment_id = @deployment_id AND ground_truth IS NOT NULL AND at > @since
GROUP BY 1 ORDER BY 1;
