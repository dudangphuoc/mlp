-- mlp:query InsertNotification :exec
INSERT INTO notify.notifications (id, workspace_id, user_id, kind, title, link, resource_uri, severity, created_at, updated_at)
VALUES (@id, @workspace_id, @user_id, @kind, @title, @link, @resource_uri, @severity, @now, @now);
