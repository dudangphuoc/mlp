-- mlp:query ListEndpointKeys :many
-- mlp:no-workspace the caller has loaded the deployment within the workspace
SELECT id, name, prefix, created_at, revoked_at FROM serving.endpoint_keys WHERE deployment_id = @deployment_id ORDER BY created_at DESC;
