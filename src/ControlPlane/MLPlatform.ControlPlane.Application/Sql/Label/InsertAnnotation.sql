-- mlp:query InsertAnnotation :exec
INSERT INTO label.annotations (id, dataset_id, path, schema_version, kind, class_id, geometry, attributes, source, confidence, created_by, created_at)
VALUES (@id, @dataset_id, @path, @schema_version, @kind, @class_id, @geometry::jsonb, @attributes::jsonb, @source, @confidence, @created_by, @now);
