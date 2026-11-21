-- mlp:query InsertRun :scalar
INSERT INTO pipeline.runs (id, pipeline_id, workspace_id, number, state, params, trigger, created_by, created_at)
VALUES (@id, @pipeline_id, @workspace_id, (SELECT coalesce(max(number), 0) + 1 FROM pipeline.runs WHERE pipeline_id = @pipeline_id),
        'running', @run_params::jsonb, @trigger, @created_by, @now)
RETURNING number;
