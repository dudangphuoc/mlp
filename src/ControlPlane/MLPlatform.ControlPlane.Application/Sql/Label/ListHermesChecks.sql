-- mlp:query ListHermesChecks :many
-- Annotator-only members pass their user id and see checks of their own items only.
SELECT c.id, c.project_id, c.path, c.revision, c.trigger, c.verdict, c.confidence, c.decided_by, c.summary, c.steps::text AS steps_json, c.suggestion, c.ai_run_id, c.action, c.actor_id, coalesce(nullif(a.display_name, ''), a.username) AS actor_name, c.created_at, c.undone_by, coalesce(nullif(u.display_name, ''), u.username) AS undone_by_name, c.undone_at, c.explanation, c.explanation_lang, c.explanation_run_id, c.explained_at
FROM label.hermes_checks c LEFT JOIN identity.users a ON a.id = c.actor_id LEFT JOIN identity.users u ON u.id = c.undone_by
WHERE c.workspace_id = @workspace_id AND c.project_id = @project_id
  AND (@verdict = '' OR c.verdict = @verdict OR (@verdict = 'auto_accepted' AND c.action = 'auto_accepted') OR (@verdict = 'undone' AND c.action = 'undone'))
  AND (@path = '' OR c.path = @path)
  AND (@only_annotator::uuid IS NULL OR EXISTS (SELECT 1 FROM label.item_states s WHERE s.project_id = c.project_id AND s.path = c.path AND s.annotator_id = @only_annotator))
ORDER BY c.created_at DESC LIMIT @lim;
