-- mlp:query CopyVersionFilesAsReferences :exec
-- mlp:no-workspace the caller has already loaded both versions within the workspace
-- One statement instead of one insert per file: a split of a few thousand files took most of a minute row by row.
INSERT INTO data.version_files (version_id, path, size_bytes, url)
SELECT @version_id, f.path, f.size_bytes, coalesce(f.url, @base_url || f.path)
FROM data.version_files f WHERE f.version_id = @source_version_id AND f.path <> @skip_path;
