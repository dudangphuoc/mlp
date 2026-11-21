-- mlp:query TouchDeployment :exec
UPDATE serving.deployments SET last_used_at = @now WHERE id = @id;
