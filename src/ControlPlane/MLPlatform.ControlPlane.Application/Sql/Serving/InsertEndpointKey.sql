-- mlp:query InsertEndpointKey :exec
INSERT INTO serving.endpoint_keys (id, deployment_id, name, prefix, hash, created_by, created_at)
VALUES (@id, @deployment_id, @name, @prefix, @hash, @created_by, @now);
