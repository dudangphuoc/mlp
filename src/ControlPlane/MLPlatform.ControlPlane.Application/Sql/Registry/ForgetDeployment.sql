-- mlp:query ForgetDeployment :exec
-- mlp:no-workspace the caller has loaded the deployment within the workspace (deployment deleted)
UPDATE registry.evaluations SET deployment_id = NULL WHERE deployment_id = @deployment_id;
