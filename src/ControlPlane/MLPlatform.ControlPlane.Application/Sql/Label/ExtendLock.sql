-- mlp:query ExtendLock :exec
UPDATE label.item_states SET lock_expires_at = @expires_at WHERE project_id = @project_id AND path = @path AND lock_owner = @user_id;
