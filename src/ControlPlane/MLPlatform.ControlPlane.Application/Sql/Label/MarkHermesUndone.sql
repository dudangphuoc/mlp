-- mlp:query MarkHermesUndone :exec
UPDATE label.hermes_checks SET action = 'undone', undone_by = @undone_by, undone_at = @now WHERE id = @id AND workspace_id = @workspace_id;
