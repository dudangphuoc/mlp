-- mlp:query DeleteMember :scalar
WITH r AS (DELETE FROM identity.memberships WHERE workspace_id = @workspace_id AND user_id = @user_id AND role <> 'owner' RETURNING 1)
SELECT count(*) FROM r;
