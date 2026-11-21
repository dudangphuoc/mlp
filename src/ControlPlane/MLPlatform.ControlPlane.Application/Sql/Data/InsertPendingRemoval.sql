-- mlp:query InsertPendingRemoval :exec
-- mlp:no-workspace the caller has loaded the dataset within the workspace
INSERT INTO data.pending_removals (dataset_id, path, at) VALUES (@dataset_id, @path, @now) ON CONFLICT DO NOTHING;
