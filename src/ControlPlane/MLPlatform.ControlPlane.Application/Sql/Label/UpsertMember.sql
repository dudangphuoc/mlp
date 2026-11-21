-- mlp:query UpsertMember :exec
INSERT INTO label.project_members (project_id, user_id, role, max_open_items) VALUES (@project_id, @user_id, @role, @max_open_items)
ON CONFLICT (project_id, user_id, role) DO UPDATE SET max_open_items = excluded.max_open_items;
