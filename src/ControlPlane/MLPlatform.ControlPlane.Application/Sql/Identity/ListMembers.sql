-- mlp:query ListMembers :many
SELECT u.id AS user_id, u.username, u.display_name, u.email, m.role
FROM identity.memberships m JOIN identity.users u ON u.id = m.user_id
WHERE m.workspace_id = @workspace_id ORDER BY u.username;
