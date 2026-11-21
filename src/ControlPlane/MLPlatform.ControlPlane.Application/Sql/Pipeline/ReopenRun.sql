-- mlp:query ReopenRun :exec
UPDATE pipeline.runs SET state = 'running', error = NULL, finished_at = NULL, params = @run_params::jsonb WHERE id = @id;
