-- mlp:query InsertView :scalar
INSERT INTO feature.views (id, workspace_id, name, version, entity_id, source_version_id, timestamp_column, features, ttl_hours, online, materialize_every_min, state, created_by, created_at)
VALUES (@id, @workspace_id, @name, (SELECT coalesce(max(version), 0) + 1 FROM feature.views WHERE workspace_id = @workspace_id AND name = @name),
        @entity_id, @source_version_id, @timestamp_column, @features::jsonb, @ttl_hours, @online, @materialize_every_min, 'pending', @created_by, @now)
RETURNING version;
