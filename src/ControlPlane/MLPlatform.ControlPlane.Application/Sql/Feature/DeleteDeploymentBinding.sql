-- mlp:query DeleteDeploymentBinding :exec
-- mlp:no-workspace the caller has loaded the deployment within the workspace (deployment deleted)
DELETE FROM feature.bindings WHERE deployment_id = @deployment_id;
