-- mlp:query DeleteDeploymentData :exec
-- mlp:no-workspace the caller has loaded the deployment within the workspace (deployment deleted)
WITH p AS (DELETE FROM monitor.predictions WHERE deployment_id = @deployment_id),
     s AS (DELETE FROM monitor.snapshots WHERE deployment_id = @deployment_id)
DELETE FROM monitor.configs WHERE deployment_id = @deployment_id;
