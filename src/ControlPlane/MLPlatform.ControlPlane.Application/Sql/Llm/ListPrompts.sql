-- mlp:query ListPrompts :many
SELECT p.id, p.name, p.description, p.created_at, coalesce(max(v.version), 0) AS latest_version
FROM llmops.prompts p LEFT JOIN llmops.prompt_versions v ON v.prompt_id = p.id
WHERE p.workspace_id = @workspace_id GROUP BY p.id ORDER BY p.name;
