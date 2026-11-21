-- mlp:query BumpRevision :exec
UPDATE label.item_states SET revision = revision + 1, draft_annotations = NULL, draft_by = NULL, draft_saved_at = NULL
WHERE project_id = @project_id AND path = @path;
