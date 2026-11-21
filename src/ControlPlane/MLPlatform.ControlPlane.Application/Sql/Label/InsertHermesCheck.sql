-- mlp:query InsertHermesCheck :exec
INSERT INTO label.hermes_checks (id, workspace_id, project_id, path, revision, trigger, verdict, confidence, decided_by, summary, steps, suggestion, ai_run_id, action, actor_id, created_at)
VALUES (@id, @workspace_id, @project_id, @path, @revision, @trigger, @verdict, @confidence, @decided_by, @summary, @steps_json::jsonb, @suggestion, @ai_run_id, @action, @actor_id, @now);
