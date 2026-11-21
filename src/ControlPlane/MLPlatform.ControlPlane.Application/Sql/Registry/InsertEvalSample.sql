-- mlp:query InsertEvalSample :exec
INSERT INTO registry.evaluation_samples (evaluation_id, idx, path, width, height, gt, pred, tp, fp, fn, correct, error, latency_ms)
VALUES (@evaluation_id, @idx, @path, @width, @height, @gt::jsonb, @pred::jsonb, @tp, @fp, @fn, @correct, @error, @latency_ms)
ON CONFLICT (evaluation_id, idx) DO NOTHING;
UPDATE registry.evaluations e SET
  done = (SELECT count(*) FROM registry.evaluation_samples s WHERE s.evaluation_id = e.id),
  errors = (SELECT count(*) FROM registry.evaluation_samples s WHERE s.evaluation_id = e.id AND s.error IS NOT NULL)
WHERE e.id = @evaluation_id;
