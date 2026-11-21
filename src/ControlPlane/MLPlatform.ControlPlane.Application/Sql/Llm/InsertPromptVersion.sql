-- mlp:query InsertPromptVersion :scalar
INSERT INTO llmops.prompt_versions (prompt_id, version, system, template, variables, deployment_id, temperature, max_tokens, note, created_by, created_at)
VALUES (@prompt_id, (SELECT coalesce(max(version), 0) + 1 FROM llmops.prompt_versions WHERE prompt_id = @prompt_id),
        @system, @template, @variables::jsonb, @deployment_id, @temperature, @max_tokens, @note, @created_by, @now)
RETURNING version;
