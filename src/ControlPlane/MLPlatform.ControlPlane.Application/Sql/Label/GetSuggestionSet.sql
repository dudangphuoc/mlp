-- mlp:query GetSuggestionSet :one
SELECT s.id, s.project_id, s.path, s.revision, s.source_kind, s.source_id, s.source_label, s.external, s.state, s.detail, s.items::text AS items_json, s.unmapped::text AS unmapped_json, s.ai_run_id, s.latency_ms, s.job_id, s.decision, coalesce(nullif(u.display_name, ''), u.username) AS created_by_name, s.created_at, s.decided_at
FROM label.suggestion_sets s LEFT JOIN identity.users u ON u.id = s.created_by
WHERE s.id = @id AND s.workspace_id = @workspace_id;
