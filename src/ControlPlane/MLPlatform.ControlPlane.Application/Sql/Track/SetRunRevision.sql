-- mlp:query SetRunRevision :exec
UPDATE track.runs SET revision_id = @revision_id, train_kind = @train_kind, start_kind = @start_kind, retrain_of_run_id = @retrain_of_run_id, host_id = @host_id
WHERE id = @id AND workspace_id = @workspace_id;
