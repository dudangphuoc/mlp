-- mlp:query ClaimNextItem :one
-- Pull queue (§4.1): own items (in progress / rejected) first, then unassigned or expired locks; SKIP LOCKED
-- ensures two people clicking "Next" at the same time never get the same item.
WITH c AS (
  SELECT path FROM label.item_states
  WHERE project_id = @project_id AND (
        (annotator_id = @user_id AND status IN ('in_progress', 'rejected'))
     OR status = 'unlabeled'
     OR (status = 'in_progress' AND lock_expires_at < @now))
  ORDER BY CASE WHEN annotator_id = @user_id THEN 0 ELSE 1 END, priority_score DESC, path
  LIMIT 1 FOR UPDATE SKIP LOCKED)
UPDATE label.item_states s
SET status = 'in_progress', annotator_id = @user_id, lock_owner = @user_id, lock_expires_at = @expires_at
FROM c WHERE s.project_id = @project_id AND s.path = c.path
RETURNING s.path;
