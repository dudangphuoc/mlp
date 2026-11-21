-- mlp:query ClearPendingChanges :exec
-- mlp:no-workspace the caller has loaded the dataset within the workspace
WITH a AS (DELETE FROM data.files WHERE dataset_id = @dataset_id)
DELETE FROM data.pending_removals WHERE dataset_id = @dataset_id;
