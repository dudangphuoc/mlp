-- mlp:query SetEvaluationTotal :exec
UPDATE registry.evaluations SET total = @total WHERE id = @id;
