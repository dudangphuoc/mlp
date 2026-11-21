-- mlp:query ListMemberships :many
SELECT w.id, w.slug, w.name, w.kind, w.created_at, m.role
FROM identity.memberships m JOIN identity.workspaces w ON w.id = m.workspace_id
WHERE m.user_id = @user_id ORDER BY w.created_at;
