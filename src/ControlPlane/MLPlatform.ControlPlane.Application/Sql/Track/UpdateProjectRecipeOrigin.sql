-- mlp:query UpdateProjectRecipeOrigin :exec
UPDATE track.project_recipes SET origin_recipe_id = @origin_recipe_id, origin_manifest = @origin_manifest::jsonb, origin_manifest_sha256 = @origin_manifest_sha256
WHERE id = @id AND workspace_id = @workspace_id;
