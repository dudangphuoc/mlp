-- mlp:query InsertVersionFile :exec
INSERT INTO data.version_files (version_id, path, size_bytes) VALUES (@version_id, @path, @size_bytes);
