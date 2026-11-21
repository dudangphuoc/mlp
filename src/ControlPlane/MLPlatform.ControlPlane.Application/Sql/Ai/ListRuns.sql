-- mlp:query ListRuns :many
-- mlp:no-workspace filtered by workspace and/or instance-level runs, decided by the caller's rights
SELECT r.id, r.workspace_id, r.kind, r.purpose, r.subject, r.trigger, r.actor_id, coalesce(nullif(u.display_name, ''), u.username) AS actor_name, r.state, r.fallback, r.model_ref, r.model_label, r.selection::text AS selection_json, r.input::text AS input_json, r.output::text AS output_json, r.decided_by, r.confidence, r.latency_ms, r.tokens_in, r.tokens_out, r.cost_usd, r.attempts::text AS attempts_json, r.error_code, r.error, r.started_at, r.finished_at
FROM ai.runs r LEFT JOIN identity.users u ON u.id = r.actor_id
WHERE ((@workspace_id::uuid IS NOT NULL AND r.workspace_id = @workspace_id) OR (@include_instance AND r.workspace_id IS NULL))
  AND (@state = '' OR r.state = @state OR (@state = 'fallback' AND r.fallback IS NOT NULL))
  AND (@model_ref::uuid IS NULL OR r.model_ref = @model_ref)
ORDER BY r.started_at DESC LIMIT @lim;
