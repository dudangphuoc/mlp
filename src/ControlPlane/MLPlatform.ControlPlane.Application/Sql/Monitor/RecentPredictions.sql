-- mlp:query RecentPredictions :many
SELECT id, at, latency_ms, features::text AS features, prediction::text AS prediction, ground_truth::text AS ground_truth
FROM monitor.predictions WHERE deployment_id = @deployment_id ORDER BY at DESC, id DESC LIMIT @limit;
