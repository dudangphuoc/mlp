-- mlp:query InsertLabelSet :scalar
INSERT INTO label.label_sets (id, dataset_id, number, schema_version, snapshot_key, item_count, annotation_count, include_status, created_by, created_at)
VALUES (@id, @dataset_id, (SELECT coalesce(max(number), 0) + 1 FROM label.label_sets WHERE dataset_id = @dataset_id),
        @schema_version, @snapshot_key, @item_count, @annotation_count, @include_status, @created_by, @now)
RETURNING number;
