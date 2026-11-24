-- mlp:query GetCodeRun :one
SELECT id, workspace_id, state FROM track.runs WHERE id = @id AND workspace_id = @workspace_id AND origin = 'code';
