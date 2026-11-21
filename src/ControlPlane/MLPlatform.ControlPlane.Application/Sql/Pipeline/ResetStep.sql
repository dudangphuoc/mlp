-- mlp:query ResetStep :exec
UPDATE pipeline.steps SET state = 'pending', attempt = 0, ref_id = NULL, output = '{}', error = NULL, started_at = NULL, finished_at = NULL
WHERE run_id = @run_id AND step_id = @step_id;
