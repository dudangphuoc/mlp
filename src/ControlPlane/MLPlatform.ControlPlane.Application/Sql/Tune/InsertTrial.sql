-- mlp:query InsertTrial :exec
INSERT INTO tune.trials (sweep_id, number, params, run_id, state, error, created_at)
VALUES (@sweep_id, @number, @trial_params::jsonb, @run_id, @state, @error, @now);
