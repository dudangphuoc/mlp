-- mlp:query InsertStep :exec
INSERT INTO pipeline.steps (run_id, step_id, kind, state) VALUES (@run_id, @step_id, @kind, 'pending');
