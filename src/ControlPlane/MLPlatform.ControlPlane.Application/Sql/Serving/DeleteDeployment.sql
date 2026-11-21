-- mlp:query DeleteDeployment :exec
-- mlp:no-workspace the caller has loaded the stopped deployment within the workspace
WITH k AS (DELETE FROM serving.endpoint_keys WHERE deployment_id = @id)
DELETE FROM serving.deployments WHERE id = @id AND state IN ('stopped', 'failed');
