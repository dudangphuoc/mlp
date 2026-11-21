-- mlp:query SetHermesExplanation :exec
UPDATE label.hermes_checks SET explanation = @explanation, explanation_lang = @lang, explanation_run_id = @run_id, explained_at = @now
WHERE id = @id AND workspace_id = @workspace_id;
