-- mlp:query FinishExport :exec
UPDATE registry.exports SET state = @state, error = @error, file_name = @file_name, size_bytes = @size_bytes
WHERE workspace_id = @workspace_id AND id = @id AND state = 'running';
