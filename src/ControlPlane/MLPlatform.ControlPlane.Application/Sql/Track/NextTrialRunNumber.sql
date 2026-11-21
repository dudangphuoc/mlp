-- mlp:query NextTrialRunNumber :scalar
-- Hidden trial runs count down from -1 per project, so they never take a visible #n nor clash with UNIQUE (project_id, number).
WITH p AS (SELECT id FROM track.projects WHERE id = @project_id AND workspace_id = @workspace_id FOR UPDATE)
SELECT (LEAST(COALESCE(min(r.number), 0), 0) - 1)::bigint FROM p LEFT JOIN track.runs r ON r.project_id = p.id AND r.workspace_id = @workspace_id AND r.number <= 0;
