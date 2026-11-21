-- mlp:query UpdateImport :exec
-- mlp:no-workspace internal worker updates by job id
UPDATE hub.imports SET state = @state, target_id = coalesce(@target_id, target_id), files_total = @files_total, files_done = @files_done,
    bytes_total = @bytes_total, bytes_done = @bytes_done, message = @message, updated_at = @now,
    finished_at = CASE WHEN @state IN ('succeeded', 'failed') THEN @now ELSE finished_at END
WHERE id = @id;
