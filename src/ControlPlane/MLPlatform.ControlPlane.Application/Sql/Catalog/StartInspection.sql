-- mlp:query StartInspection :exec
INSERT INTO catalog.inspections (id, workspace_id, recipe_name, env_tag, variant, params_sha256, num_classes, state, created_at)
VALUES (@id, @workspace_id, @recipe_name, @env_tag, @variant, @params_sha256, @num_classes, 'running', @now)
ON CONFLICT (workspace_id, env_tag, variant, params_sha256, num_classes) DO UPDATE SET state = 'running', error = '',
    overlay_yaml = '', config_json = '', blocks = '[]', sources = '{}', created_at = excluded.created_at
WHERE catalog.inspections.workspace_id = @workspace_id;
