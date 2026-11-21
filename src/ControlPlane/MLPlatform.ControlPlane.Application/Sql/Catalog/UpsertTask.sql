-- mlp:query UpsertTask :exec
INSERT INTO catalog.tasks (key, modality_in, modality_out, converters, primary_metric, metrics)
VALUES (@key, @modality_in, @modality_out, @converters, @primary_metric, @metrics)
ON CONFLICT (key) DO UPDATE SET modality_in = excluded.modality_in, modality_out = excluded.modality_out,
    converters = excluded.converters, primary_metric = excluded.primary_metric, metrics = excluded.metrics;
