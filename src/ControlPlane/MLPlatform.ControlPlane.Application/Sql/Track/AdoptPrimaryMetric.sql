-- mlp:query AdoptPrimaryMetric :exec
-- A code run declares no main metric: the first one it logs becomes the main metric.
UPDATE track.runs SET primary_metric_key = @key WHERE id = @id AND workspace_id = @workspace_id AND primary_metric_key = '';
