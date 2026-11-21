-- mlp:query ListPendingRemovals :many
-- mlp:no-workspace the caller has loaded the dataset within the workspace
SELECT path FROM data.pending_removals WHERE dataset_id = @dataset_id ORDER BY path;
