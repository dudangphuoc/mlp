-- mlp:query ModelStats :many
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
-- Last 24 hours per model: runs, failures, fallbacks, latency p50/p95.
SELECT model_ref AS model_id, count(*) AS runs, count(*) FILTER (WHERE state = 'failed') AS failures, count(*) FILTER (WHERE fallback IS NOT NULL) AS fallbacks,
       coalesce(percentile_cont(0.5) WITHIN GROUP (ORDER BY latency_ms), 0)::int AS p50_ms,
       coalesce(percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms), 0)::int AS p95_ms
FROM ai.runs WHERE model_ref IS NOT NULL AND started_at > @since GROUP BY model_ref;
