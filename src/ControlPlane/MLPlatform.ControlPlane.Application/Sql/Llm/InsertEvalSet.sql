-- mlp:query InsertEvalSet :exec
INSERT INTO llmops.eval_sets (id, workspace_id, name, cases, created_by, created_at) VALUES (@id, @workspace_id, @name, @cases::jsonb, @created_by, @now);
