-- mlp:query OnlineSample :many
SELECT entity_key, event_ts, values::text AS values FROM feature.online WHERE view_id = @view_id ORDER BY entity_key LIMIT @limit;
