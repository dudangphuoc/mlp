-- mlp:query FinishInspection :exec
UPDATE catalog.inspections SET state = @state, error = @error, overlay_yaml = @overlay_yaml, config_json = @config_json,
    blocks = @blocks::jsonb, sources = @sources::jsonb
WHERE workspace_id = @workspace_id AND env_tag = @env_tag AND variant = @variant AND params_sha256 = @params_sha256
  AND num_classes = @num_classes;
