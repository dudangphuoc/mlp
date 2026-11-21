-- mlp:query UpsertPreference :exec
INSERT INTO notify.preferences (user_id, workspace_id, kind, channel, enabled) VALUES (@user_id, @workspace_id, @kind, 'in_app', @enabled)
ON CONFLICT (user_id, workspace_id, kind, channel) DO UPDATE SET enabled = excluded.enabled;
