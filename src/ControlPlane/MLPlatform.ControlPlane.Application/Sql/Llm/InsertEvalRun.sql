-- mlp:query InsertEvalRun :exec
INSERT INTO llmops.eval_runs (id, workspace_id, set_id, prompt_id, prompt_version, score, results, created_by, created_at)
VALUES (@id, @workspace_id, @set_id, @prompt_id, @prompt_version, @score, @results::jsonb, @created_by, @now);
