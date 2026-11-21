-- mlp:query DecideSuggestionSet :exec
UPDATE label.suggestion_sets SET decision = @decision, decided_by = @decided_by, decided_at = @now WHERE id = @id AND workspace_id = @workspace_id;
