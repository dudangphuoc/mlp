-- mlp:query UpsertAssistSettings :exec
INSERT INTO label.assist_settings (project_id, workspace_id, sources, class_mapping, allow_external, min_confidence, updated_by, updated_at)
VALUES (@project_id, @workspace_id, @sources_json::jsonb, @class_mapping_json::jsonb, @allow_external, @min_confidence, @updated_by, @now)
ON CONFLICT (project_id) DO UPDATE SET sources = excluded.sources, class_mapping = excluded.class_mapping, allow_external = excluded.allow_external,
    min_confidence = excluded.min_confidence, updated_by = excluded.updated_by, updated_at = excluded.updated_at;
