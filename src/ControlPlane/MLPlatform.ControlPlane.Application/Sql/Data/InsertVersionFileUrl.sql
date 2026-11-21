-- mlp:query InsertVersionFileUrl :exec
INSERT INTO data.version_files (version_id, path, size_bytes, url) VALUES (@version_id, @path, @size_bytes, @url);
