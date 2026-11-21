-- mlp:query ListFiles :many
SELECT path, size_bytes, uploaded_at FROM data.files WHERE dataset_id = @dataset_id ORDER BY path;
