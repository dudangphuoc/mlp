-- mlp:query ExportAnnotations :many
SELECT a.path, a.kind, a.class_id, a.geometry::text AS geometry, a.attributes::text AS attributes, a.source
FROM label.annotations a
WHERE a.dataset_id = @dataset_id AND a.deleted_at IS NULL AND EXISTS (
  SELECT 1 FROM label.item_states s WHERE s.project_id = @project_id AND s.path = a.path
    AND (s.status = 'accepted' OR (@all_labeled AND s.status IN ('in_review', 'accepted'))))
ORDER BY a.path, a.created_at, a.id;
