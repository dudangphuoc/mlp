-- mlp:query ClearRoleDefault :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
DELETE FROM ai.role_defaults WHERE role = @role;
