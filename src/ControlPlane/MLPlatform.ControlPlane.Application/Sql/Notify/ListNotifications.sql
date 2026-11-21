-- mlp:query ListNotifications :many
SELECT id, kind, title, link, resource_uri, severity, count, created_at, updated_at, read_at FROM notify.notifications
WHERE user_id = @user_id AND workspace_id = @workspace_id AND (NOT @unread_only OR read_at IS NULL)
ORDER BY updated_at DESC LIMIT 100;
