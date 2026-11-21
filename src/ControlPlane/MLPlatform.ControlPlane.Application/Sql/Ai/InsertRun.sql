-- mlp:query InsertRun :exec
-- mlp:no-workspace runs may be instance-level (workspace_id null)
INSERT INTO ai.runs (id, workspace_id, kind, purpose, subject, trigger, actor_id, state, model_ref, model_label, selection, input, started_at)
VALUES (@id, @workspace_id, @kind, @purpose, @subject, @trigger, @actor_id, 'running', @model_ref, @model_label, @selection_json::jsonb, @input_json::jsonb, @now);
