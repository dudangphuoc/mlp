-- mlp:query InsertSnapshot :exec
INSERT INTO monitor.snapshots (id, deployment_id, at, samples, status, drift, quality)
VALUES (@id, @deployment_id, @now, @samples, @status, @drift::jsonb, @quality::jsonb);
