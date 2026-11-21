-- mlp:query InsertReferenceVersion :exec
INSERT INTO data.dataset_versions (id, dataset_id, number, prefix, file_count, size_bytes, note, created_by, created_at, storage, source_ref)
VALUES (@id, @dataset_id, @number, '', @file_count, @size_bytes, @note, @created_by, @now, 'reference', @source_ref);
