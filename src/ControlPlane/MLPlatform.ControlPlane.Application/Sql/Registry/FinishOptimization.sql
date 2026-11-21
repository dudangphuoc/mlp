-- mlp:query FinishOptimization :exec
UPDATE registry.optimizations SET state = @state, error = @error, report = @report::jsonb
WHERE workspace_id = @workspace_id AND id = @id AND state = 'running';
