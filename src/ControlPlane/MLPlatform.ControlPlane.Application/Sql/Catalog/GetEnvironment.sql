-- mlp:query GetEnvironment :one
SELECT id, workspace_id, recipe_name, env_tag, host_id, state, error, started_at, built_at
FROM catalog.environments
WHERE workspace_id = @workspace_id AND env_tag = @env_tag AND host_id = @host_id;
