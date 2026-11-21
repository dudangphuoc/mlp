-- mlp:query FinishMaterialize :exec
UPDATE feature.views SET state = @state, state_reason = @reason, rows = @rows, entities = @entities, last_materialized_at = @now,
  max_event_ts = coalesce((SELECT max(event_ts) FROM feature.offline WHERE view_id = @id), max_event_ts)
WHERE id = @id;
