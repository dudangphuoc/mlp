-- mlp:query UpsertEnvironment :exec
INSERT INTO catalog.environments (id, workspace_id, recipe_name, env_tag, host_id, state, error, started_at, built_at)
VALUES (@id, @workspace_id, @recipe_name, @env_tag, @host_id, @state, @error, @started_at, @built_at)
ON CONFLICT (workspace_id, env_tag, host_id) DO UPDATE SET recipe_name = excluded.recipe_name, state = excluded.state,
    error = excluded.error, started_at = excluded.started_at, built_at = excluded.built_at
WHERE catalog.environments.workspace_id = @workspace_id;
