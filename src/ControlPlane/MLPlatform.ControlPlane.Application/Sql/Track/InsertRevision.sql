-- mlp:query InsertRevision :exec
INSERT INTO track.revisions (id, workspace_id, project_recipe_id, number, parent_revision_id, manifest, manifest_sha256, variant, params, content_sha256,
                             trial_key, created_by, created_at)
VALUES (@id, @workspace_id, @project_recipe_id, @number, @parent_revision_id, @manifest::jsonb, @manifest_sha256, @variant, @params_json::jsonb, @content_sha256,
        @trial_key, @created_by, @now);
