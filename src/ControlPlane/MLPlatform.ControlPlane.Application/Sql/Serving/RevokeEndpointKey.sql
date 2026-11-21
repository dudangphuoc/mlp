-- mlp:query RevokeEndpointKey :exec
-- mlp:no-workspace the caller has loaded the deployment within the workspace
UPDATE serving.endpoint_keys SET revoked_at = @now WHERE id = @id AND deployment_id = @deployment_id AND revoked_at IS NULL;
