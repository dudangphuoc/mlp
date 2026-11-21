-- mlp:query UpdateItemState :exec
UPDATE label.item_states SET status = @status, lock_owner = @lock_owner, lock_expires_at = @lock_expires_at,
  reviewer_id = @reviewer_id, skip_reason = @skip_reason,
  submitted_at = CASE WHEN @mark_submitted THEN @now ELSE submitted_at END,
  reviewed_at = CASE WHEN @mark_reviewed THEN @now ELSE reviewed_at END,
  priority_score = CASE WHEN @status = 'rejected' THEN priority_score + 1000 ELSE priority_score END
WHERE project_id = @project_id AND path = @path;
