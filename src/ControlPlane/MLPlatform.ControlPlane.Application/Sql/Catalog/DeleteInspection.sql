-- mlp:query DeleteInspection :exec
DELETE FROM catalog.inspections
WHERE workspace_id = @workspace_id AND env_tag = @env_tag AND variant = @variant AND params_sha256 = @params_sha256
  AND num_classes = @num_classes;
