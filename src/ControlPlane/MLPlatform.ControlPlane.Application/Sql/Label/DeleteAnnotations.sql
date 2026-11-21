-- mlp:query DeleteAnnotations :exec
UPDATE label.annotations SET deleted_at = @now WHERE dataset_id = @dataset_id AND path = @path AND deleted_at IS NULL;
