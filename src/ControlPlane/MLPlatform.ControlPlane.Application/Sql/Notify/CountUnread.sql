-- mlp:query CountUnread :scalar
SELECT count(*) FROM notify.notifications WHERE user_id = @user_id AND workspace_id = @workspace_id AND read_at IS NULL;
