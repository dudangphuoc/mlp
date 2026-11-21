-- mlp:query GetRevision :one
SELECT id, project_recipe_id, number, parent_revision_id, manifest::text AS manifest, manifest_sha256, variant, params::text AS params, content_sha256, created_by, created_at FROM track.revisions WHERE id = @id AND workspace_id = @workspace_id;
