-- mlp:query ListVersionFiles :many
SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id ORDER BY path;
