-- mlp:query FinishRun :exec
UPDATE pipeline.runs SET state = @state, error = @error, finished_at = @now WHERE id = @id AND state = 'running';
