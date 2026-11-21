-- mlp:query ListRecipes :many
SELECT id, workspace_id, name, display_name, family, description, maturity, tasks, license_code, license_weights, license_notes,
       requires_ack, gpu, min_vram_gb, source, manifest::text AS manifest, state, selfcheck
FROM catalog.recipes
WHERE enabled AND (workspace_id IS NULL OR workspace_id = @workspace_id) AND (@task = '' OR @task = ANY(tasks))
ORDER BY workspace_id NULLS FIRST, family, name;
