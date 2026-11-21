-- mlp:query UpsertBinding :exec
INSERT INTO feature.bindings (deployment_id, workspace_id, entity_id, features, updated_by, updated_at)
VALUES (@deployment_id, @workspace_id, @entity_id, @features::jsonb, @updated_by, @now)
ON CONFLICT (deployment_id) DO UPDATE SET entity_id = excluded.entity_id, features = excluded.features, updated_by = excluded.updated_by, updated_at = excluded.updated_at;
