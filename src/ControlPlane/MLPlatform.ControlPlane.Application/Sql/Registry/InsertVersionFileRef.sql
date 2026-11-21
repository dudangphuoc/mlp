-- mlp:query InsertVersionFileRef :exec
-- mlp:no-workspace the import worker writes files of a version it reserved in the workspace
INSERT INTO registry.version_files (version_id, path, size_bytes, url) VALUES (@version_id, @path, @size_bytes, @url);
