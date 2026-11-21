-- mlp:query ClaimNextReview :one
WITH c AS (
  SELECT path FROM label.item_states
  WHERE project_id = @project_id AND annotator_id <> @user_id AND (
        (status = 'in_review' AND (reviewer_id IS NULL OR reviewer_id = @user_id OR lock_expires_at < @now)))
  ORDER BY CASE WHEN reviewer_id = @user_id THEN 0 ELSE 1 END, submitted_at, path
  LIMIT 1 FOR UPDATE SKIP LOCKED)
UPDATE label.item_states s SET reviewer_id = @user_id, lock_owner = @user_id, lock_expires_at = @expires_at
FROM c WHERE s.project_id = @project_id AND s.path = c.path
RETURNING s.path;
