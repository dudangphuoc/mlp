-- mlp:query InsertMembership :exec
INSERT INTO identity.memberships (workspace_id, user_id, role, joined_at)
VALUES (@workspace_id, @user_id, @role, @joined_at);
