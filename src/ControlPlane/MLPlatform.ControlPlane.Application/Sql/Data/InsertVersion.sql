-- mlp:query InsertVersion :exec
INSERT INTO data.dataset_versions (id, dataset_id, number, prefix, file_count, size_bytes, note, created_by, created_at)
VALUES (@id, @dataset_id, @number, @prefix, @file_count, @size_bytes, @note, @created_by, @now);
