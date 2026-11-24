-- mlp:query ListApiKeys :many
SELECT s.id, s.name, s.created_at, s.last_seen_at, s.key_workspace_id AS workspace_id, w.name AS workspace_name
FROM identity.sessions s LEFT JOIN identity.workspaces w ON w.id = s.key_workspace_id
WHERE s.user_id = @user_id AND s.kind = 'api_key' AND s.revoked_at IS NULL ORDER BY s.created_at DESC;
