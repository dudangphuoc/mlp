-- mlp:query GetRevisionByContent :one
SELECT id, project_recipe_id, number, parent_revision_id, manifest::text AS manifest, manifest_sha256, variant, params::text AS params, content_sha256, created_by, created_at FROM track.revisions WHERE project_recipe_id = @project_recipe_id AND workspace_id = @workspace_id AND content_sha256 = @content_sha256;
