-- mlp:query SetVersionSplit :exec
-- mlp:no-workspace the caller has already loaded this version within the workspace
UPDATE data.dataset_versions SET split_state = @split_state, split_counts = @split_counts::jsonb, val_sha256 = @val_sha256 WHERE id = @id;
