-- mlp:query GetEvalSet :one
SELECT id, name, cases::text AS cases, created_at FROM llmops.eval_sets WHERE id = @id AND workspace_id = @workspace_id;
