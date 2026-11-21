-- mlp:query SaveDraft :exec
UPDATE label.item_states SET draft_annotations = @draft::jsonb, draft_by = @user_id, draft_saved_at = @now
WHERE project_id = @project_id AND path = @path;
