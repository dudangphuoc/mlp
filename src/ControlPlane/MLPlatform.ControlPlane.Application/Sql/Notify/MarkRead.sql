-- mlp:query MarkRead :exec
UPDATE notify.notifications SET read_at = @now WHERE user_id = @user_id AND workspace_id = @workspace_id AND read_at IS NULL
  AND (@all OR id = ANY(@ids));
