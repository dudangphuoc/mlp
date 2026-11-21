-- mlp:query GetKeyDeployment :scalar
SELECT deployment_id FROM serving.endpoint_keys WHERE hash = @hash AND revoked_at IS NULL;
