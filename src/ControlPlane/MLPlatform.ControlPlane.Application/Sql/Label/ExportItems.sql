-- mlp:query ExportItems :many
-- Items whose labels are final (accepted), or every submitted item when all_labeled.
SELECT path FROM label.item_states
WHERE project_id = @project_id AND (status = 'accepted' OR (@all_labeled AND status IN ('in_review', 'accepted'))) ORDER BY path;
