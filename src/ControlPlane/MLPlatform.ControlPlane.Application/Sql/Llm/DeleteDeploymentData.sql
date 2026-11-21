-- mlp:query DeleteDeploymentData :exec
-- mlp:no-workspace the caller has loaded the deployment within the workspace (deployment deleted)
WITH s AS (DELETE FROM llmops.settings WHERE deployment_id = @deployment_id),
     u AS (DELETE FROM llmops.usage WHERE deployment_id = @deployment_id)
UPDATE llmops.prompt_versions SET deployment_id = NULL WHERE deployment_id = @deployment_id;
