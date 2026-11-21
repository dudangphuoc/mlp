-- mlp:query GetHermesCheck :one
SELECT c.id, c.project_id, c.path, c.revision, c.trigger, c.verdict, c.confidence, c.decided_by, c.summary, c.steps::text AS steps_json, c.suggestion, c.ai_run_id, c.action, c.actor_id, coalesce(nullif(a.display_name, ''), a.username) AS actor_name, c.created_at, c.undone_by, coalesce(nullif(u.display_name, ''), u.username) AS undone_by_name, c.undone_at, c.explanation, c.explanation_lang, c.explanation_run_id, c.explained_at
FROM label.hermes_checks c LEFT JOIN identity.users a ON a.id = c.actor_id LEFT JOIN identity.users u ON u.id = c.undone_by
WHERE c.id = @id AND c.workspace_id = @workspace_id;
