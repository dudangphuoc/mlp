-- mlp:query SetRoleDefault :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
INSERT INTO ai.role_defaults (role, model_id, set_by, set_at) VALUES (@role, @model_id, @set_by, @now)
ON CONFLICT (role) DO UPDATE SET model_id = excluded.model_id, set_by = excluded.set_by, set_at = excluded.set_at;
