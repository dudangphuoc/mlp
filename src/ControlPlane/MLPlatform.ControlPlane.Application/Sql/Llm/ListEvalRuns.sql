-- mlp:query ListEvalRuns :many
SELECT r.id, r.set_id, s.name AS set_name, r.prompt_id, p.name AS prompt_name, r.prompt_version, r.score, r.results::text AS results, r.created_at
FROM llmops.eval_runs r JOIN llmops.eval_sets s ON s.id = r.set_id JOIN llmops.prompts p ON p.id = r.prompt_id
WHERE r.workspace_id = @workspace_id ORDER BY r.created_at DESC LIMIT 100;
