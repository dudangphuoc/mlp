-- mlp:query SetVersionTotals :exec
-- mlp:no-workspace the caller has just created the version within the workspace
UPDATE data.dataset_versions SET file_count = (SELECT count(*) FROM data.version_files WHERE version_id = @id),
       size_bytes = (SELECT coalesce(sum(size_bytes), 0) FROM data.version_files WHERE version_id = @id)
WHERE id = @id;
