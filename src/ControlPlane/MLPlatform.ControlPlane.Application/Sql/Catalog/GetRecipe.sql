-- mlp:query GetRecipe :one
SELECT id, workspace_id, name, display_name, family, description, maturity, tasks, license_code, license_weights, license_notes,
       requires_ack, gpu, min_vram_gb, source, manifest::text AS manifest, state, selfcheck
FROM catalog.recipes
WHERE enabled AND name = @name AND (workspace_id IS NULL OR workspace_id = @workspace_id)
ORDER BY workspace_id NULLS LAST LIMIT 1;
