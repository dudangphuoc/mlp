-- mlp:query SetDatasetStatus :exec
UPDATE data.datasets SET status = @status WHERE id = @id AND workspace_id = @workspace_id;
