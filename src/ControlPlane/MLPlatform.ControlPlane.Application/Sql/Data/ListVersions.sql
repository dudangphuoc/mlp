-- mlp:query ListVersions :many
SELECT id, dataset_id, number, prefix, file_count, size_bytes, note, created_at, storage, source_ref
FROM data.dataset_versions WHERE dataset_id = @dataset_id ORDER BY number DESC;
