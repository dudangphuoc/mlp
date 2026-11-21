-- mlp:query InsertPrediction :exec
INSERT INTO monitor.predictions (id, deployment_id, model_version, at, latency_ms, features, prediction)
VALUES (@id, @deployment_id, @model_version, @now, @latency_ms, @features::jsonb, @prediction::jsonb);
