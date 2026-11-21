-- mlp:query DeleteRun :exec
DELETE FROM track.metrics WHERE run_id IN (SELECT id FROM track.runs WHERE id = @id AND workspace_id = @workspace_id);
DELETE FROM track.run_artifacts WHERE run_id IN (SELECT id FROM track.runs WHERE id = @id AND workspace_id = @workspace_id);
DELETE FROM track.runs WHERE id = @id AND workspace_id = @workspace_id;
