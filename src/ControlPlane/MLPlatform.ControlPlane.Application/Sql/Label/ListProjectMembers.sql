-- mlp:query ListProjectMembers :many
SELECT pm.user_id, u.username, pm.role, pm.max_open_items,
       (SELECT count(*) FROM label.item_states s WHERE s.project_id = pm.project_id AND s.annotator_id = pm.user_id
          AND s.status IN ('in_progress', 'rejected')) AS open_items
FROM label.project_members pm JOIN identity.users u ON u.id = pm.user_id
WHERE pm.project_id = @project_id ORDER BY u.username, pm.role;
