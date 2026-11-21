-- mlp:query MarkTriggered :exec
UPDATE pipeline.pipelines SET last_triggered_at = @now WHERE id = @id;
