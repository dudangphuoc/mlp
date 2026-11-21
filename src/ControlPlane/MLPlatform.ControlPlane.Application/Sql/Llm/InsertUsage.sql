-- mlp:query InsertUsage :exec
INSERT INTO llmops.usage (id, workspace_id, deployment_id, prompt_id, prompt_version, source, at, prompt_tokens, completion_tokens, latency_ms, cost, blocked, flags)
VALUES (@id, @workspace_id, @deployment_id, @prompt_id, @prompt_version, @source, @now, @prompt_tokens, @completion_tokens, @latency_ms, @cost, @blocked, @flags::jsonb);
