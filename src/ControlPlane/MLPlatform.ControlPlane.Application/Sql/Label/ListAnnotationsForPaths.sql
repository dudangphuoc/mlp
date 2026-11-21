-- mlp:query ListAnnotationsForPaths :many
-- Platform labels (M05) of several files at once, for the dataset content browser.
SELECT path, kind, class_id, geometry::text AS geometry, source
FROM label.annotations WHERE dataset_id = @dataset_id AND path = ANY(@paths) AND deleted_at IS NULL ORDER BY path, created_at, id;
