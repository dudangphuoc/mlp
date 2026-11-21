-- mlp:query RecipeUsage :many
-- Projects of the workspace that work with a Catalog recipe (its "used in" tab): their copy, latest revision and runs.
SELECT p.id AS project_id, p.name AS project_name, pr.id AS project_recipe_id, pr.created_at,
       (SELECT max(v.number) FROM track.revisions v WHERE v.project_recipe_id = pr.id AND v.workspace_id = pr.workspace_id) AS latest_revision,
       (SELECT count(*) FROM track.runs r WHERE r.project_id = p.id AND r.workspace_id = pr.workspace_id AND r.recipe = pr.origin_name AND NOT r.is_trial) AS run_count,
       (SELECT max(r.created_at) FROM track.runs r WHERE r.project_id = p.id AND r.workspace_id = pr.workspace_id AND r.recipe = pr.origin_name AND NOT r.is_trial) AS last_run_at
FROM track.project_recipes pr
JOIN track.projects p ON p.id = pr.project_id AND p.workspace_id = pr.workspace_id
WHERE pr.workspace_id = @workspace_id AND pr.origin_name = @recipe
ORDER BY last_run_at DESC NULLS LAST, p.name;
