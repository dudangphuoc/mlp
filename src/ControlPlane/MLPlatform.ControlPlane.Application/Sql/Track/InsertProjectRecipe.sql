-- mlp:query InsertProjectRecipe :exec
INSERT INTO track.project_recipes (id, workspace_id, project_id, origin_recipe_id, origin_name, origin_manifest, origin_manifest_sha256, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @origin_recipe_id, @origin_name, @origin_manifest::jsonb, @origin_manifest_sha256, @created_by, @now);
