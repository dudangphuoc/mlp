-- mlp:query ListPromptVersions :many
SELECT version, system, template, variables::text AS variables, deployment_id, temperature, max_tokens, note, created_at
FROM llmops.prompt_versions WHERE prompt_id = @prompt_id ORDER BY version DESC;
