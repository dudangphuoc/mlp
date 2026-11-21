-- mlp:query MarkDataCacheRemoved :exec
UPDATE ops.data_syncs SET removed_at = @now
WHERE workspace_id = @workspace_id AND host_id = @host_id AND dataset_id = @dataset_id AND removed_at IS NULL;
