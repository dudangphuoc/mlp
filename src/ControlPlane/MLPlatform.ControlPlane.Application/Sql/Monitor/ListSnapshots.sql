-- mlp:query ListSnapshots :many
SELECT id, at, samples, status, drift::text AS drift, quality::text AS quality
FROM monitor.snapshots WHERE deployment_id = @deployment_id ORDER BY at DESC LIMIT @limit;
