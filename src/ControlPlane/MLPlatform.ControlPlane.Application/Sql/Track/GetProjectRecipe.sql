-- mlp:query GetProjectRecipe :one
SELECT pr.id, pr.workspace_id, pr.project_id, pr.origin_recipe_id, pr.origin_name, pr.origin_manifest::text AS origin_manifest, pr.origin_manifest_sha256, pr.created_at,
       (SELECT coalesce(max(v.number), 0) FROM track.revisions v WHERE v.project_recipe_id = pr.id AND v.workspace_id = pr.workspace_id) AS latest_revision
FROM track.project_recipes pr WHERE pr.id = @id AND pr.workspace_id = @workspace_id;
