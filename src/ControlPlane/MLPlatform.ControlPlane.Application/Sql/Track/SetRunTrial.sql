-- mlp:query SetRunTrial :exec
-- The passed trial that opened the gate for this real run.
UPDATE track.runs SET trial_id = @trial_id WHERE id = @id AND workspace_id = @workspace_id;
