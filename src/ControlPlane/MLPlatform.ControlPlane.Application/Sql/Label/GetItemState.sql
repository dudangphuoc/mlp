-- mlp:query GetItemState :one
SELECT project_id, dataset_id, path, status, annotator_id, reviewer_id, lock_owner, lock_expires_at, draft_by,
       draft_annotations::text AS draft_annotations, draft_saved_at, skip_reason, revision
FROM label.item_states WHERE project_id = @project_id AND path = @path;
