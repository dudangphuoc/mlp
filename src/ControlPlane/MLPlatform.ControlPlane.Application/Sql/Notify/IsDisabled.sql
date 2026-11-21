-- mlp:query IsDisabled :scalar
SELECT count(*) FROM notify.preferences
WHERE user_id = @user_id AND workspace_id = @workspace_id AND kind = @kind AND channel = 'in_app' AND NOT enabled;
