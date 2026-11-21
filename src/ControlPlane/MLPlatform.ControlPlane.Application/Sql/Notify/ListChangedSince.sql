-- mlp:query ListChangedSince :many
SELECT id, kind, title, link, resource_uri, severity, count, created_at, updated_at, read_at FROM notify.notifications
WHERE user_id = @user_id AND workspace_id = @workspace_id AND updated_at > @since ORDER BY updated_at;
