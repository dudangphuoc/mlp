-- mlp:query ListEvalSets :many
SELECT id, name, cases::text AS cases, created_at FROM llmops.eval_sets WHERE workspace_id = @workspace_id ORDER BY name;
