-- mlp:query ListEvalSamples :many
-- Worst first (most misses + false alarms, then wrong class), optionally only samples with an error.
SELECT s.idx, s.path, s.width, s.height, s.gt::text AS gt, s.pred::text AS pred, s.tp, s.fp, s.fn, s.correct, s.error, s.latency_ms
FROM registry.evaluation_samples s
WHERE s.evaluation_id = @evaluation_id AND (NOT @errors_only OR s.fp + s.fn > 0 OR NOT s.correct OR s.error IS NOT NULL)
ORDER BY (s.error IS NOT NULL) DESC, s.fp + s.fn DESC, s.correct, s.idx
OFFSET @offset LIMIT @limit;
