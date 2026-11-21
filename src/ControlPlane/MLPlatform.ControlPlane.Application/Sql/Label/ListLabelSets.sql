-- mlp:query ListLabelSets :many
SELECT id, number, schema_version, item_count, annotation_count, include_status, created_at
FROM label.label_sets WHERE dataset_id = @dataset_id ORDER BY number DESC;
