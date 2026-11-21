-- mlp:query SetProjectsStateOfDataset :exec
UPDATE label.projects SET state = @state WHERE dataset_id = @dataset_id AND workspace_id = @workspace_id;
