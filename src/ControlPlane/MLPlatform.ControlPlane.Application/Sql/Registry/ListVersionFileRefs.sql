-- mlp:query ListVersionFileRefs :many
-- mlp:no-workspace caller already loaded the version within its workspace
SELECT path, size_bytes, url FROM registry.version_files WHERE version_id = @version_id ORDER BY path;
