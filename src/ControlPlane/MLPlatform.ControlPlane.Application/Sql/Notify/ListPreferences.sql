-- mlp:query ListPreferences :many
SELECT kind, enabled FROM notify.preferences WHERE user_id = @user_id AND workspace_id = @workspace_id AND channel = 'in_app';
