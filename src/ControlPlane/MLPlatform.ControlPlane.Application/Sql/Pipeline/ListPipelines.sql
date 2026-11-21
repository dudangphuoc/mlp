-- mlp:query ListPipelines :many
SELECT id, workspace_id, name, definition::text AS definition, trigger::text AS trigger, enabled, last_triggered_at, created_by, created_at, updated_at FROM pipeline.pipelines WHERE workspace_id = @workspace_id ORDER BY name;
