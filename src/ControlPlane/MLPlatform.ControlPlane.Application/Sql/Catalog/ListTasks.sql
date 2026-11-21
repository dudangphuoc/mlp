-- mlp:query ListTasks :many
SELECT key, modality_in, modality_out, converters, primary_metric, metrics FROM catalog.tasks ORDER BY key;
