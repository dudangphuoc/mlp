-- mlp:query ListEnvironments :many
SELECT id, workspace_id, recipe_name, env_tag, host_id, state, error, started_at, built_at
FROM catalog.environments
WHERE workspace_id = @workspace_id AND recipe_name = @recipe_name
ORDER BY started_at DESC;
