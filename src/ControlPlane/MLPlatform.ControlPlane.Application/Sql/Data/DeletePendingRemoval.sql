-- mlp:query DeletePendingRemoval :exec
-- mlp:no-workspace the caller has loaded the dataset within the workspace
DELETE FROM data.pending_removals WHERE dataset_id = @dataset_id AND path = @path;
