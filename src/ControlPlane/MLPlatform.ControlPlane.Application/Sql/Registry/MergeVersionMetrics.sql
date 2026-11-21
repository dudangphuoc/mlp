-- mlp:query MergeVersionMetrics :exec
UPDATE registry.versions SET metrics = metrics || @metrics::jsonb WHERE id = @id;
