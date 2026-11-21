-- mlp:query ResolveView :one
-- View name (+ version; 0 = latest ready version, or latest if none is ready yet).
SELECT v.id, v.workspace_id, v.name, v.version, v.entity_id, e.name AS entity_name, e.join_key, v.source_version_id, v.timestamp_column, v.features::text AS features, v.ttl_hours, v.online, v.materialize_every_min, v.state, v.state_reason, v.rows, v.entities, v.last_materialized_at, v.max_event_ts, v.created_at FROM feature.views v JOIN feature.entities e ON e.id = v.entity_id
WHERE v.workspace_id = @workspace_id AND v.name = @name AND (@version = 0 OR v.version = @version)
ORDER BY (v.state = 'ready') DESC, v.version DESC LIMIT 1;
