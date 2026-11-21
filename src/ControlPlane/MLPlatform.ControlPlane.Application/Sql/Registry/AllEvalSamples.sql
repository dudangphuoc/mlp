-- mlp:query AllEvalSamples :many
SELECT idx, path, width, height, gt::text AS gt, pred::text AS pred, tp, fp, fn, correct, error, latency_ms
FROM registry.evaluation_samples WHERE evaluation_id = @evaluation_id ORDER BY idx;
