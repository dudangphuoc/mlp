-- mlp:query NextRunNumber :scalar
-- Locks the project row so two trainings started at the same moment never get the same number.
WITH p AS (SELECT id FROM track.projects WHERE id = @project_id AND workspace_id = @workspace_id FOR UPDATE)
SELECT (COALESCE(max(r.number), 0) + 1)::bigint FROM p LEFT JOIN track.runs r ON r.project_id = p.id AND r.workspace_id = @workspace_id AND NOT r.is_trial;
