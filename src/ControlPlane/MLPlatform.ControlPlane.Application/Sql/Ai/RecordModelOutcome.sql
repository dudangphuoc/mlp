-- mlp:query RecordModelOutcome :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
-- Health follows real runs: a success resets it to ok; 3 failures in a row mark the model failed (1–2 = degraded).
UPDATE ai.models SET
    consecutive_failures = CASE WHEN @ok THEN 0 ELSE consecutive_failures + 1 END,
    health = CASE WHEN @ok THEN 'ok' WHEN consecutive_failures + 1 >= 3 THEN 'failed' ELSE 'degraded' END,
    health_reason = CASE WHEN @ok THEN NULL ELSE @reason END,
    health_at = @now
WHERE id = @id;
