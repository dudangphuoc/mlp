-- mlp:query GetPipeline :one
SELECT id, workspace_id, name, definition::text AS definition, trigger::text AS trigger, enabled, last_triggered_at, created_by, created_at, updated_at FROM pipeline.pipelines WHERE id = @id AND workspace_id = @workspace_id;
