-- mlp:query ClaimItem :one
-- mlp:no-workspace the caller has loaded the project within the workspace
-- Open one chosen item for editing (not the next one in the queue): free, or its lock expired, or already the caller's. A labeled or
-- reviewed item goes back to "in progress" and has to be submitted again.
UPDATE label.item_states SET status = 'in_progress', annotator_id = @user_id, lock_owner = @user_id, lock_expires_at = @expires_at
WHERE project_id = @project_id AND path = @path
  AND (lock_owner IS NULL OR lock_owner = @user_id OR lock_expires_at < @now)
RETURNING path;
