-- mlp:query CompleteEvaluation :exec
UPDATE registry.evaluations SET state = 'succeeded', state_reason = NULL, metrics = @metrics::jsonb, per_class = @per_class::jsonb,
  confusion = @confusion::jsonb, finished_at = @now
WHERE id = @id;
