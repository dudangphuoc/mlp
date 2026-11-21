-- mlp:query CountItems :scalar
SELECT count(*) FROM label.item_states s
WHERE s.project_id = @project_id AND (@status = '' OR s.status = @status) AND (@search = '' OR s.path ILIKE '%' || @search || '%')
  AND (NOT @mine OR s.annotator_id = @user_id OR s.reviewer_id = @user_id);
