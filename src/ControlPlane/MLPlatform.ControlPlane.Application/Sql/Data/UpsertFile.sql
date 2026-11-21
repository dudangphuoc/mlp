-- mlp:query UpsertFile :exec
INSERT INTO data.files (dataset_id, path, size_bytes, uploaded_at) VALUES (@dataset_id, @path, @size_bytes, @now)
ON CONFLICT (dataset_id, path) DO UPDATE SET size_bytes = excluded.size_bytes, uploaded_at = excluded.uploaded_at;
