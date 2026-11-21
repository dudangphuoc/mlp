-- mlp:query ListRevisions :many
SELECT v.number, v.variant, v.params::text AS params, p.number AS parent, v.created_by, v.created_at,
       (SELECT count(*) FROM track.runs r WHERE r.revision_id = v.id AND r.workspace_id = v.workspace_id AND NOT r.is_trial) AS run_count
FROM track.revisions v LEFT JOIN track.revisions p ON p.id = v.parent_revision_id AND p.workspace_id = v.workspace_id
WHERE v.project_recipe_id = @project_recipe_id AND v.workspace_id = @workspace_id ORDER BY v.number DESC;
