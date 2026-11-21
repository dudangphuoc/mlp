-- mlp:query UpdateStep :exec
UPDATE pipeline.steps SET state = @state, attempt = @attempt, ref_id = @ref_id, output = @output::jsonb, error = @error,
  started_at = CASE WHEN @state = 'running' AND started_at IS NULL THEN @now ELSE started_at END,
  finished_at = CASE WHEN @state IN ('succeeded', 'failed', 'skipped', 'gated') THEN @now ELSE NULL END
WHERE run_id = @run_id AND step_id = @step_id;
