-- mlp:query WorkspaceAdmins :many
SELECT user_id AS id FROM identity.memberships WHERE workspace_id = @workspace_id AND role IN ('owner', 'admin');
