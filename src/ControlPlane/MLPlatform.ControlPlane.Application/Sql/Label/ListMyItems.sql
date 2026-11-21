-- mlp:query ListMyItems :many
SELECT path, status, revision FROM label.item_states
WHERE project_id = @project_id AND (annotator_id = @user_id OR reviewer_id = @user_id) ORDER BY path;
