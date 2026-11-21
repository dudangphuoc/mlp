-- mlp:query MarkRunTrial :exec
UPDATE track.runs SET is_trial = true WHERE id = @id AND workspace_id = @workspace_id;
