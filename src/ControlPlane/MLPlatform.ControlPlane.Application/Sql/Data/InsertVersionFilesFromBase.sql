-- mlp:query InsertVersionFilesFromBase :exec
-- mlp:no-workspace the caller has loaded both versions within the workspace
-- The files of the previous version that stay: they are not copied again, the new version points at where they already are.
INSERT INTO data.version_files (version_id, path, size_bytes, url)
SELECT @version_id, f.path, f.size_bytes, coalesce(f.url, @base_url || f.path)
FROM data.version_files f WHERE f.version_id = @base_version_id AND NOT (f.path = ANY(@skip_paths));
