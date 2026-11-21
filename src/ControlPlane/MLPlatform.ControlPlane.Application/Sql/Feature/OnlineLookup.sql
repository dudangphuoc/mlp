-- mlp:query OnlineLookup :many
SELECT entity_key, event_ts, values::text AS values FROM feature.online WHERE view_id = @view_id AND entity_key = ANY(@keys);
