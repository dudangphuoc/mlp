-- mlp:query SetPrimaryMetric :exec
UPDATE track.runs SET primary_metric_value = @value WHERE id = @id;
