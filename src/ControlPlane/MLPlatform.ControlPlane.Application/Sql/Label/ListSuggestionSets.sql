-- mlp:query ListSuggestionSets :many
-- Annotator-only members pass their user id and see suggestions of their own items only.
SELECT s.id, s.project_id, s.path, s.revision, s.source_kind, s.source_id, s.source_label, s.external, s.state, s.detail, s.items::text AS items_json, s.unmapped::text AS unmapped_json, s.ai_run_id, s.latency_ms, s.job_id, s.decision, coalesce(nullif(u.display_name, ''), u.username) AS created_by_name, s.created_at, s.decided_at
FROM label.suggestion_sets s LEFT JOIN identity.users u ON u.id = s.created_by
WHERE s.workspace_id = @workspace_id AND s.project_id = @project_id AND (@path = '' OR s.path = @path)
  AND (@job_id::uuid IS NULL OR s.job_id = @job_id)
  AND (@only_annotator::uuid IS NULL OR EXISTS (SELECT 1 FROM label.item_states i WHERE i.project_id = s.project_id AND i.path = s.path AND i.annotator_id = @only_annotator))
ORDER BY s.created_at DESC LIMIT @lim;
