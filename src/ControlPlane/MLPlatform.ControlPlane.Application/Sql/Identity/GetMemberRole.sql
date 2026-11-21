-- mlp:query GetMemberRole :scalar
SELECT role FROM identity.memberships WHERE workspace_id = @workspace_id AND user_id = @user_id;
