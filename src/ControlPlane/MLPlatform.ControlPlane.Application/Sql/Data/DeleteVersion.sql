-- mlp:query DeleteVersion :exec
-- mlp:no-workspace the caller has loaded the version within the workspace and checked nothing uses it
WITH f AS (DELETE FROM data.version_files WHERE version_id = @id)
DELETE FROM data.dataset_versions WHERE id = @id;
