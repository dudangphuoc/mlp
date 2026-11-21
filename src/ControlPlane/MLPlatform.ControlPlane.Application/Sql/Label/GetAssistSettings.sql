-- mlp:query GetAssistSettings :one
SELECT project_id, sources::text AS sources_json, class_mapping::text AS class_mapping_json, allow_external, min_confidence, updated_at
FROM label.assist_settings WHERE project_id = @project_id AND workspace_id = @workspace_id;
