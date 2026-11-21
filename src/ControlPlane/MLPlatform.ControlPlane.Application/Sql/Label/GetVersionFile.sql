-- mlp:query GetVersionFile :one
-- mlp:no-workspace the caller already loaded the project (and its version) within the workspace
SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id AND path = @path;
