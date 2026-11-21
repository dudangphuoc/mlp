-- mlp:query SetMemberRole :scalar
WITH r AS (UPDATE identity.memberships SET role = @role WHERE workspace_id = @workspace_id AND user_id = @user_id AND role <> 'owner' RETURNING 1)
SELECT count(*) FROM r;
