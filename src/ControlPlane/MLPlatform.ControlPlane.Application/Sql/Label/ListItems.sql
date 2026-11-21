-- mlp:query ListItems :many
SELECT s.path, s.status, s.annotator_id, s.reviewer_id, s.submitted_at, s.reviewed_at, s.skip_reason,
       (SELECT count(*) FROM label.annotations a WHERE a.dataset_id = s.dataset_id AND a.path = s.path AND a.deleted_at IS NULL) AS labels
FROM label.item_states s
WHERE s.project_id = @project_id AND (@status = '' OR s.status = @status) AND (@search = '' OR s.path ILIKE '%' || @search || '%')
  AND (NOT @mine OR s.annotator_id = @user_id OR s.reviewer_id = @user_id)
ORDER BY s.path LIMIT @take OFFSET @skip;
