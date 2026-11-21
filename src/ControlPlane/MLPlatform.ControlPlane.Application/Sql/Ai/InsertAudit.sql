-- mlp:query InsertAudit :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
INSERT INTO ai.audit (id, actor_id, action, target, before, after, at) VALUES (@id, @actor_id, @action, @target, @before_json::jsonb, @after_json::jsonb, @now);
