-- mlp:query NextRevisionNumber :scalar
WITH p AS (SELECT id FROM track.project_recipes WHERE id = @project_recipe_id AND workspace_id = @workspace_id FOR UPDATE)
SELECT (coalesce(max(v.number), 0) + 1)::bigint FROM p LEFT JOIN track.revisions v ON v.project_recipe_id = p.id AND v.workspace_id = @workspace_id;
