-- mlp:query ListAnnotations :many
SELECT id, kind, class_id, geometry::text AS geometry, attributes::text AS attributes, source, confidence
FROM label.annotations WHERE dataset_id = @dataset_id AND path = @path AND deleted_at IS NULL ORDER BY created_at, id;
