-- mlp:query ListRoleDefaults :many
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
SELECT role, model_id FROM ai.role_defaults ORDER BY role;
