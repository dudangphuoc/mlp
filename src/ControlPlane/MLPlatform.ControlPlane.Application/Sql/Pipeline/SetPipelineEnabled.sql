-- mlp:query SetPipelineEnabled :exec
UPDATE pipeline.pipelines SET enabled = @enabled, updated_at = @now WHERE id = @id;
