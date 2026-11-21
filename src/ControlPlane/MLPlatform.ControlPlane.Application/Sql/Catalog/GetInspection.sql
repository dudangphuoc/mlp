-- mlp:query GetInspection :one
SELECT id, workspace_id, recipe_name, env_tag, variant, params_sha256, num_classes, state, error, overlay_yaml, config_json,
       blocks::text AS blocks, created_at, sources::text AS sources
FROM catalog.inspections
WHERE workspace_id = @workspace_id AND env_tag = @env_tag AND variant = @variant AND params_sha256 = @params_sha256
  AND num_classes = @num_classes;
